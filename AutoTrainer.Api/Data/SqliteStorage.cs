using AutoTrainer.Api.Options;
using Microsoft.Data.Sqlite;

namespace AutoTrainer.Api.Data;

public interface ISqliteStorage
{
    string RootPath { get; }
    string DevicePath { get; }
    string AnimalsPath { get; }
    string DeviceDatabasePath { get; }
    string DeviceConnectionString { get; }

    string GetAnimalDatabasePath(string identifier);
    string GetAnimalConnectionString(string identifier);

    bool EnsureDirectories(out string? error);
}

public class SqliteStorage : ISqliteStorage
{
    private readonly ILogger<SqliteStorage> _logger;

    public SqliteStorage(IOptions<DataOptions> options, ILogger<SqliteStorage> logger)
    {
        _logger = logger;

        var location = options.Value.SQLLiteLocation;

        RootPath = string.IsNullOrWhiteSpace(location)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(location);
    }

    public string RootPath { get; }

    public string DevicePath => Path.Combine(RootPath, "device");

    public string AnimalsPath => Path.Combine(RootPath, "animals");

    public string DeviceDatabasePath => Path.Combine(DevicePath, "device.sqlite");

    public string DeviceConnectionString =>
        new SqliteConnectionStringBuilder { DataSource = DeviceDatabasePath }.ConnectionString;

    public string GetAnimalDatabasePath(string identifier) =>
        Path.Combine(AnimalsPath, identifier + ".sqlite");

    public string GetAnimalConnectionString(string identifier) =>
        new SqliteConnectionStringBuilder { DataSource = GetAnimalDatabasePath(identifier) }.ConnectionString;

    public bool EnsureDirectories(out string? error)
    {
        foreach (var path in new[] { RootPath, DevicePath, AnimalsPath })
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                // Path does not exist and could not be created (e.g. missing parent, read-only filesystem).
                error = $"Data location '{path}' does not exist and could not be created: {ex.Message}";
                return false;
            }

            // CreateDirectory is a no-op on an existing directory, so it does not surface a
            // read-only/permission problem. Probe with an actual write to catch that case.
            if (!IsWritable(path, out var writeError))
            {
                error = $"Data location '{path}' is not writable: {writeError}";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool IsWritable(string path, out string? error)
    {
        var probe = Path.Combine(path, $".write-probe-{Guid.NewGuid():N}");

        try
        {
            using (File.Create(probe)) { }
            File.Delete(probe);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // Identifiers come from message data and are used to build a file path under animals/.
    // Reject anything that could escape that directory or that is not a valid file name.
    public static bool IsValidIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        if (identifier.Contains("..", StringComparison.Ordinal))
            return false;

        if (identifier.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        return true;
    }
}
