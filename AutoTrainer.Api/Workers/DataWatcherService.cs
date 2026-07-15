using AutoTrainer.Api.Models;

namespace AutoTrainer.Api.Workers;

public partial class DataWatcherService(AutotrainerDevice device, ILogger<DataWatcherService> logger)
    : BackgroundService
{
    private string _root = "";
    private readonly SemaphoreSlim _scanSemaphore = new(1, 1);

    private CancellationTokenSource? _reinitCts;
    private FileSystemWatcher? _imageWatcher;

    private string? CurrentDeviceDataPath { get; set; }

    private string? CurrentTrialPath { get; set; }

    private string? CurrentWebImagesPath { get; set; }

    private string? CurrentLatestWebImage { get; set; }

    private string DeviceId => device.Configuration.DeviceId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            LogWaitingForConfiguration();

            // Recheck every 10 seconds, but only log the "still waiting" message
            // every 6 cycles (60 seconds) to reduce log file spam.
            const int logEveryCycles = 6;
            var waitCycle = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                var location = device.Configuration.DataLocation;
                var deviceId = DeviceId;

                if (deviceId.Length == 0)
                {
                    if (waitCycle % logEveryCycles == 0)
                    {
                        LogWaitingForDeviceId();
                    }
                }
                else if (location.Length > 0)
                {
                    if (Directory.Exists(location))
                    {
                        break;
                    }
                    else if (waitCycle % logEveryCycles == 0)
                    {
                        var fileInfo = new FileInfo(location);
                        LogFileInfoExists(fileInfo.Exists);

                        // For a directory
                        var dirInfo = new DirectoryInfo(location);
                        LogDirectoryExists(dirInfo.Exists);

                        LogWaitingForLocationCreated(location);
                    }
                }
                else if (waitCycle % logEveryCycles == 0)
                {
                    LogWaitingForLocationSet();
                }

                waitCycle++;

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;   // shutting down; the check below returns out of the worker
                }
            }

            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            _root = device.Configuration.DataLocation;

            await ScanAsync();

            using var reinitCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            _reinitCts = reinitCts;

            using var watcher = new FileSystemWatcher(_root);
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.DirectoryName;

            watcher.Created += OnDirectoryCreated;
            watcher.Renamed += OnDirectoryRenamed;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;

            LogWatchingDataRoot(_root, DeviceId);

            try
            {
                await Task.Delay(Timeout.Infinite, reinitCts.Token);
            }
            catch (OperationCanceledException)
            {
                // A reinit (data location changed) loops to rebuild the watcher; a shutdown (stopping token)
                // lets the outer loop condition end the worker. Only the reinit case is worth logging.
                if (!stoppingToken.IsCancellationRequested)
                    LogDataLocationChanged(device.Configuration.DataLocation);
            }
            finally
            {
                _reinitCts = null;
                StopImageWatcher();
            }
        }
    }

    private async void OnDirectoryCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            await HandleDirectoryChangeAsync(e.FullPath);
        }
        catch (Exception ex)
        {
            LogDirectoryCreationError(ex, e.FullPath);
        }
    }

    private async void OnDirectoryRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            await HandleDirectoryChangeAsync(e.FullPath);
        }
        catch (Exception ex)
        {
            LogDirectoryRenameError(ex, e.FullPath);
        }
    }

    private async void OnWatcherError(object sender, ErrorEventArgs e)
    {
        try
        {
            LogWatcherError(e.GetException());
            await ScanAsync();
        }
        catch (Exception ex)
        {
            LogWatcherErrorHandlingFailed(ex);
        }
    }

    private async Task HandleDirectoryChangeAsync(string fullPath)
    {
        if (device.Configuration.DataLocation != _root)
        {
            _reinitCts?.Cancel();

            return;
        }

        var relativePath = Path.GetRelativePath(_root, fullPath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var deviceId = DeviceId;

        switch (parts.Length)
        {
            // YYYYMMDD date folder at root level
            case 1 when IsDateFolder(parts[0]):
                LogDateFolderCreated(parts[0]);
                await ScanAsync();
                break;

            // Device folder
            case 2 when IsDateFolder(parts[0]) && parts[1].Equals(deviceId, StringComparison.OrdinalIgnoreCase):
                LogDeviceFolderCreated(relativePath);
                await ScanAsync();
                break;

            // Trial folder
            case 3 when IsDateFolder(parts[0]) && parts[1].Equals(deviceId, StringComparison.OrdinalIgnoreCase) && IsTrialFolder(parts[2]):
                LogTrialFolderCreated(relativePath);
                await ScanAsync();
                break;

            // Web images folder
            case 3 when IsDateFolder(parts[0]) && parts[1].Equals(deviceId, StringComparison.OrdinalIgnoreCase) && IsWebImagesFolder(parts[2], out _):
                LogWebImagesFolderCreated(relativePath);
                await ScanAsync();
                break;
        }
    }

    private async Task ScanAsync()
    {
        bool devicePathChanged = false;
        bool trialPathChanged = false;
        bool webImagesPathChanged = false;
        bool latestWebImageChanged = false;

        await _scanSemaphore.WaitAsync();
        try
        {
            string? newDevicePath = null;
            string? newTrialPath = null;
            string? newWebImagesPath = null;
            string? newLatestWebImage = null;

            var deviceId = DeviceId;

            var dateFolders = Directory.EnumerateDirectories(_root)
                .Select(Path.GetFileName)
                .Where(IsDateFolder)
                .OrderDescending();

            foreach (var dateFolder in dateFolders)
            {
                var datePath = Path.Combine(_root, dateFolder!);

                var deviceDir = Directory.EnumerateDirectories(datePath)
                    .FirstOrDefault(d => Path.GetFileName(d)!.Equals(deviceId, StringComparison.OrdinalIgnoreCase));

                if (deviceDir is null)
                {
                    LogDeviceNotFound(deviceId, dateFolder);
                    continue;
                }

                newDevicePath = deviceDir;

                var latestTrial = Directory.EnumerateDirectories(deviceDir)
                    .Select(Path.GetFileName)
                    .Where(IsTrialFolder)
                    .MaxBy(n => int.Parse(n!.AsSpan(5)));

                if (latestTrial is null)
                {
                    LogLatestTrialNotFound(deviceDir);
                }
                else
                {
                    newTrialPath = Path.Combine(deviceDir, latestTrial!);
                }

                var webImagesFolder = Directory.EnumerateDirectories(deviceDir)
                    .Select(Path.GetFileName)
                    .Where(n => IsWebImagesFolder(n, out _))
                    .MaxBy(n => { IsWebImagesFolder(n, out var h); return h; });

                if (webImagesFolder is not null)
                {
                    newWebImagesPath = Path.Combine(deviceDir, webImagesFolder);
                    newLatestWebImage = FindLatestPng(newWebImagesPath);
                }

                break;
            }

            if (CurrentDeviceDataPath != newDevicePath)
            {
                CurrentDeviceDataPath = newDevicePath;
                LogCurrentDeviceDataPath(newDevicePath ?? "(none)");
                devicePathChanged = true;
            }

            if (CurrentTrialPath != newTrialPath)
            {
                CurrentTrialPath = newTrialPath;
                LogCurrentTrialPath(newTrialPath ?? "(none)");
                trialPathChanged = true;
            }

            if (CurrentWebImagesPath != newWebImagesPath)
            {
                CurrentWebImagesPath = newWebImagesPath;
                LogCurrentWebImagesPath(newWebImagesPath ?? "(none)");
                webImagesPathChanged = true;
            }

            if (CurrentLatestWebImage != newLatestWebImage)
            {
                CurrentLatestWebImage = newLatestWebImage;
                LogCurrentLatestWebImage(newLatestWebImage ?? "(none)");
                latestWebImageChanged = true;
            }
        }
        catch (Exception ex)
        {
            LogScanError(ex);
        }
        finally
        {
            _scanSemaphore.Release();
        }

        if (devicePathChanged)
        {
            device.OnDeviceDataPathChanged(CurrentDeviceDataPath);
        }

        if (trialPathChanged)
        {
            device.OnTrialPathChanged(CurrentTrialPath);
        }

        if (webImagesPathChanged)
        {
            device.OnWebImagesPathChanged(CurrentWebImagesPath);

            if (CurrentWebImagesPath is not null)
            {
                StartImageWatcher(CurrentWebImagesPath);
            }
            else
            {
                StopImageWatcher();
            }
        }

        if (latestWebImageChanged)
        {
            device.OnLatestWebImageChanged(CurrentLatestWebImage);
        }
    }

    private void StartImageWatcher(string webImagesPath)
    {
        StopImageWatcher();

        _imageWatcher = new FileSystemWatcher(webImagesPath)
        {
            Filter = "*.png",
            NotifyFilter = NotifyFilters.FileName
        };

        _imageWatcher.Created += OnImageCreated;
        _imageWatcher.Error += OnImageWatcherError;
        _imageWatcher.EnableRaisingEvents = true;

        LogWatchingWebImages(webImagesPath);
    }

    private void StopImageWatcher()
    {
        if (_imageWatcher is not null)
        {
            _imageWatcher.EnableRaisingEvents = false;
            _imageWatcher.Dispose();
            _imageWatcher = null;
        }
    }

    private void OnImageCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Serve the previous image (N-1) since we know it's fully written.
            // The newly detected image (N) may still be mid-write.
            var previous = CurrentLatestWebImage;
            CurrentLatestWebImage = e.FullPath;

            if (previous is not null)
            {
                device.OnLatestWebImageChanged(previous);
            }
        }
        catch (Exception ex)
        {
            LogImageCreationError(ex, e.FullPath);
        }
    }

    private void OnImageWatcherError(object sender, ErrorEventArgs e)
    {
        try
        {
            LogImageWatcherError(e.GetException());

            if (CurrentWebImagesPath is not null && Directory.Exists(CurrentWebImagesPath))
            {
                StartImageWatcher(CurrentWebImagesPath);
            }
        }
        catch (Exception ex)
        {
            LogImageWatcherErrorHandlingFailed(ex);
        }
    }

    private static bool IsDateFolder(string? name) =>
        name is { Length: 8 } && DateOnly.TryParseExact(name, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsTrialFolder(string? name) =>
        name is not null && name.Length >= 6 && name.StartsWith("trial", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(5), out _);

    private static bool IsWebImagesFolder(string? name, out int hour)
    {
        hour = -1;

        if (name is null || !name.EndsWith("_web_images", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Expect pattern: <prefix>hNN_web_images — at least 3 chars before the suffix (hNN)
        var prefix = name.AsSpan(0, name.Length - "_web_images".Length);

        if (prefix.Length < 3 || (prefix[^3] != 'h' && prefix[^3] != 'H'))
        {
            return false;
        }

        return int.TryParse(prefix[^2..], out hour) && hour is >= 0 and <= 23;
    }

    private static string? FindLatestPng(string directory)
    {
        return Directory.EnumerateFiles(directory, "*.png")
            .MaxBy(GetPngTimestamp);
    }

    private static string GetPngTimestamp(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var webIdx = name.LastIndexOf("_web_", StringComparison.OrdinalIgnoreCase);

        if (webIdx < 0)
        {
            return "";
        }

        return name[(webIdx + 5)..];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for device data location to be configured")]
    private partial void LogWaitingForConfiguration();

    [LoggerMessage(Level = LogLevel.Information, Message = "FileInfo.Exists: {exists}")]
    private partial void LogFileInfoExists(bool exists);

    [LoggerMessage(Level = LogLevel.Information, Message = "directory.Exists: {exists}")]
    private partial void LogDirectoryExists(bool exists);

    [LoggerMessage(Level = LogLevel.Information, Message = "Still waiting for {location} to be created")]
    private partial void LogWaitingForLocationCreated(string location);

    [LoggerMessage(Level = LogLevel.Information, Message = "Still waiting for device data location to be set")]
    private partial void LogWaitingForLocationSet();

    [LoggerMessage(Level = LogLevel.Information, Message = "Still waiting for device id to be set")]
    private partial void LogWaitingForDeviceId();

    [LoggerMessage(Level = LogLevel.Information, Message = "Watching data root {root} for device {deviceId}")]
    private partial void LogWatchingDataRoot(string root, string deviceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Data location changed to {location}, reinitializing watcher")]
    private partial void LogDataLocationChanged(string location);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error handling directory creation: {path}")]
    private partial void LogDirectoryCreationError(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error handling directory rename: {path}")]
    private partial void LogDirectoryRenameError(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "FileSystemWatcher error, rescanning")]
    private partial void LogWatcherError(Exception? ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error handling watcher error event")]
    private partial void LogWatcherErrorHandlingFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Date folder created: {name}")]
    private partial void LogDateFolderCreated(string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Device folder created: {path}")]
    private partial void LogDeviceFolderCreated(string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Trial folder created: {path}")]
    private partial void LogTrialFolderCreated(string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Web images folder created: {path}")]
    private partial void LogWebImagesFolderCreated(string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "{device} not found in {folder}")]
    private partial void LogDeviceNotFound(string device, string? folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "latestTrial not found in {deviceDir}")]
    private partial void LogLatestTrialNotFound(string deviceDir);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Current device data path: {path}")]
    private partial void LogCurrentDeviceDataPath(string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Current trial path: {path}")]
    private partial void LogCurrentTrialPath(string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Current web images path: {path}")]
    private partial void LogCurrentWebImagesPath(string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Current latest web image: {path}")]
    private partial void LogCurrentLatestWebImage(string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error scanning data directory")]
    private partial void LogScanError(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Watching web images directory {path}")]
    private partial void LogWatchingWebImages(string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error handling image creation: {path}")]
    private partial void LogImageCreationError(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Image watcher error, restarting")]
    private partial void LogImageWatcherError(Exception? ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error handling image watcher error")]
    private partial void LogImageWatcherErrorHandlingFailed(Exception ex);
}
