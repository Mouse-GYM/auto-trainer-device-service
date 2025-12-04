using System.Globalization;
using AutoTrainer.Api.Models;
using AutoTrainer.Api.Options;

namespace AutoTrainer.Api.Data;

public class DataWatcherService : BackgroundService
{
    private string _root = "";
    private readonly string _deviceId;
    private readonly AutotrainerDevice _device;
    private readonly ILogger<DataWatcherService> _logger;
    private readonly SemaphoreSlim _scanSemaphore = new(1, 1);

    private CancellationTokenSource? _reinitCts;
    private FileSystemWatcher? _imageWatcher;
    private string? _currentDeviceDataPath;
    private string? _currentTrialPath;
    private string? _currentWebImagesPath;
    private string? _currentLatestWebImage;

    public string? CurrentDeviceDataPath => _currentDeviceDataPath;
    public string? CurrentTrialPath => _currentTrialPath;
    public string? CurrentWebImagesPath => _currentWebImagesPath;
    public string? CurrentLatestWebImage => _currentLatestWebImage;

    public DataWatcherService(IOptions<AutoTrainerOptions> autoTrainerOptions, AutotrainerDevice device, ILogger<DataWatcherService> logger)
    {
        _deviceId = autoTrainerOptions.Value.DeviceId;
        _device = device;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Waiting for device data location to be configured");

            while (!stoppingToken.IsCancellationRequested)
            {
                var location = _device.Configuration.DataLocation;

                if (location.Length > 0)
                {
                    if (Directory.Exists(location))
                    {
                        break;
                    }
                    else
                    {
                        var fileInfo = new FileInfo(location);
                        _logger.LogInformation("FileInfo.Exists: {exists}", fileInfo.Exists);

                        // For a directory
                        var dirInfo = new DirectoryInfo(location);
                        _logger.LogInformation("directory.Exists: {exists}", dirInfo.Exists);

                        _logger.LogInformation("Still waiting for {location} to be created", location);
                    }
                }
                else
                {
                    _logger.LogInformation("Still waiting for device data location to be set");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            _root = _device.Configuration.DataLocation;

            await ScanAsync();

            using var reinitCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            _reinitCts = reinitCts;

            using var watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName
            };

            watcher.Created += OnDirectoryCreated;
            watcher.Renamed += OnDirectoryRenamed;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;

            _logger.LogInformation("Watching data root {root} for device {deviceId}", _root, _deviceId);

            try
            {
                await Task.Delay(Timeout.Infinite, reinitCts.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Data location changed to {location}, reinitializing watcher", _device.Configuration.DataLocation);
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
            _logger.LogError(ex, "Error handling directory creation: {path}", e.FullPath);
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
            _logger.LogError(ex, "Error handling directory rename: {path}", e.FullPath);
        }
    }

    private async void OnWatcherError(object sender, ErrorEventArgs e)
    {
        try
        {
            _logger.LogError(e.GetException(), "FileSystemWatcher error, rescanning");
            await ScanAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling watcher error event");
        }
    }

    private async Task HandleDirectoryChangeAsync(string fullPath)
    {
        if (_device.Configuration.DataLocation != _root)
        {
            _reinitCts?.Cancel();

            return;
        }

        var relativePath = Path.GetRelativePath(_root, fullPath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        switch (parts.Length)
        {
            // YYYYMMDD date folder at root level
            case 1 when IsDateFolder(parts[0]):
                _logger.LogDebug("Date folder created: {name}", parts[0]);
                await ScanAsync();
                break;

            // Device folder
            case 2 when IsDateFolder(parts[0]) && parts[1].Equals(_deviceId, StringComparison.OrdinalIgnoreCase):
                _logger.LogDebug("Device folder created: {path}", relativePath);
                await ScanAsync();
                break;

            // Trial folder
            case 3 when IsDateFolder(parts[0]) && parts[1].Equals(_deviceId, StringComparison.OrdinalIgnoreCase) && IsTrialFolder(parts[2]):
                _logger.LogDebug("Trial folder created: {path}", relativePath);
                await ScanAsync();
                break;

            // Web images folder
            case 3 when IsDateFolder(parts[0]) && parts[1].Equals(_deviceId, StringComparison.OrdinalIgnoreCase) && IsWebImagesFolder(parts[2], out _):
                _logger.LogDebug("Web images folder created: {path}", relativePath);
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

            var dateFolders = Directory.EnumerateDirectories(_root)
                .Select(Path.GetFileName)
                .Where(IsDateFolder)
                .OrderDescending();

            foreach (var dateFolder in dateFolders)
            {
                var datePath = Path.Combine(_root, dateFolder!);

                var deviceDir = Directory.EnumerateDirectories(datePath)
                    .FirstOrDefault(d => Path.GetFileName(d)!.Equals(_deviceId, StringComparison.OrdinalIgnoreCase));

                if (deviceDir is null)
                {
                    _logger.LogInformation("{device} not found in {folder}", _deviceId, dateFolder);
                    continue;
                }

                newDevicePath = deviceDir;

                var latestTrial = Directory.EnumerateDirectories(deviceDir)
                    .Select(Path.GetFileName)
                    .Where(IsTrialFolder)
                    .MaxBy(n => int.Parse(n!.AsSpan(5)));

                if (latestTrial is null)
                {
                    _logger.LogInformation("latestTrial not found in {deviceDir}", deviceDir);
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

            if (_currentDeviceDataPath != newDevicePath)
            {
                _currentDeviceDataPath = newDevicePath;
                _logger.LogDebug("Current device data path: {path}", newDevicePath ?? "(none)");
                devicePathChanged = true;
            }

            if (_currentTrialPath != newTrialPath)
            {
                _currentTrialPath = newTrialPath;
                _logger.LogDebug("Current trial path: {path}", newTrialPath ?? "(none)");
                trialPathChanged = true;
            }

            if (_currentWebImagesPath != newWebImagesPath)
            {
                _currentWebImagesPath = newWebImagesPath;
                _logger.LogDebug("Current web images path: {path}", newWebImagesPath ?? "(none)");
                webImagesPathChanged = true;
            }

            if (_currentLatestWebImage != newLatestWebImage)
            {
                _currentLatestWebImage = newLatestWebImage;
                _logger.LogDebug("Current latest web image: {path}", newLatestWebImage ?? "(none)");
                latestWebImageChanged = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning data directory");
        }
        finally
        {
            _scanSemaphore.Release();
        }

        if (devicePathChanged)
        {
            _device.OnDeviceDataPathChanged(_currentDeviceDataPath);
        }

        if (trialPathChanged)
        {
            _device.OnTrialPathChanged(_currentTrialPath);
        }

        if (webImagesPathChanged)
        {
            _device.OnWebImagesPathChanged(_currentWebImagesPath);

            if (_currentWebImagesPath is not null)
            {
                StartImageWatcher(_currentWebImagesPath);
            }
            else
            {
                StopImageWatcher();
            }
        }

        if (latestWebImageChanged)
        {
            _device.OnLatestWebImageChanged(_currentLatestWebImage);
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

        _logger.LogInformation("Watching web images directory {path}", webImagesPath);
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
            var previous = _currentLatestWebImage;
            _currentLatestWebImage = e.FullPath;

            if (previous is not null)
            {
                _device.OnLatestWebImageChanged(previous);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling image creation: {path}", e.FullPath);
        }
    }

    private void OnImageWatcherError(object sender, ErrorEventArgs e)
    {
        try
        {
            _logger.LogError(e.GetException(), "Image watcher error, restarting");

            if (_currentWebImagesPath is not null && Directory.Exists(_currentWebImagesPath))
            {
                StartImageWatcher(_currentWebImagesPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling image watcher error");
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
}
