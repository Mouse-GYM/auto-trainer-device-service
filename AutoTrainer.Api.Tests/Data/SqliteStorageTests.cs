using AutoTrainer.Api.Data;
using AutoTrainer.Api.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoTrainer.Api.Tests.Data;

public class SqliteStorageTests
{
    private static SqliteStorage Create(string location) =>
        new(Microsoft.Extensions.Options.Options.Create(new DataOptions { SQLLiteLocation = location }), NullLogger<SqliteStorage>.Instance);

    [Fact]
    public void EmptyLocation_UsesCurrentDirectory()
    {
        var storage = Create("");
        Assert.Equal(Directory.GetCurrentDirectory(), storage.RootPath);
    }

    [Fact]
    public void ConfiguredRoot_ComposesPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "atsqltest-" + Guid.NewGuid().ToString("N"));
        var storage = Create(root);

        Assert.Equal(Path.GetFullPath(root), storage.RootPath);
        Assert.Equal(Path.Combine(storage.RootPath, "device"), storage.DevicePath);
        Assert.Equal(Path.Combine(storage.RootPath, "animals"), storage.AnimalsPath);
        Assert.Equal(Path.Combine(storage.DevicePath, "device.sqlite"), storage.DeviceDatabasePath);
        Assert.Equal(Path.Combine(storage.AnimalsPath, "abc.sqlite"), storage.GetAnimalDatabasePath("abc"));
    }

    [Fact]
    public void EnsureDirectories_CreatesDeviceAndAnimals()
    {
        var root = Path.Combine(Path.GetTempPath(), "atsqltest-" + Guid.NewGuid().ToString("N"));
        var storage = Create(root);
        try
        {
            Assert.True(storage.EnsureDirectories(out var error));
            Assert.Null(error);
            Assert.True(Directory.Exists(storage.DevicePath));
            Assert.True(Directory.Exists(storage.AnimalsPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    public void IsValidIdentifier_RejectsBadValues(string identifier)
    {
        Assert.False(SqliteStorage.IsValidIdentifier(identifier));
    }

    [Fact]
    public void IsValidIdentifier_AcceptsNormalId()
    {
        Assert.True(SqliteStorage.IsValidIdentifier("mouse-1"));
    }

    [Fact]
    public void ConnectionString_EscapesSemicolonInPath()
    {
        // ';' is a valid path char on macOS/Linux but a connection-string separator.
        var root = Path.Combine(Path.GetTempPath(), "at;sql-" + Guid.NewGuid().ToString("N"));
        var storage = Create(root);

        var parsed = new SqliteConnectionStringBuilder(storage.DeviceConnectionString);
        Assert.Equal(storage.DeviceDatabasePath, parsed.DataSource);

        var animalParsed = new SqliteConnectionStringBuilder(storage.GetAnimalConnectionString("a;b"));
        Assert.Equal(storage.GetAnimalDatabasePath("a;b"), animalParsed.DataSource);
    }
}
