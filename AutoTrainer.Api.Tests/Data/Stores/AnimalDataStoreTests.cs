using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Data;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoTrainer.Api.Tests.Data.Stores;

public class AnimalDataStoreTests : IDisposable
{
    private readonly string _root;
    private readonly SqliteStorage _storage;
    private readonly AnimalDataStore _store;

    public AnimalDataStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "atanimal-" + Guid.NewGuid().ToString("N"));
        _storage = new SqliteStorage(
            Microsoft.Extensions.Options.Options.Create(new DataOptions { SQLLiteLocation = _root }),
            NullLogger<SqliteStorage>.Instance);
        _storage.EnsureDirectories(out _);
        var factory = new AnimalDbContextFactory(_storage);
        _store = new AnimalDataStore(factory, NullLogger<AnimalDataStore>.Instance);
    }

    [Fact]
    public async Task EnsureAnimalDatabase_CreatesFile_AndIsIdempotent()
    {
        Assert.True(await _store.EnsureAnimalDatabaseAsync("mouse-1"));
        Assert.True(File.Exists(_storage.GetAnimalDatabasePath("mouse-1")));
        Assert.True(await _store.EnsureAnimalDatabaseAsync("mouse-1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../x")]
    public async Task EnsureAnimalDatabase_RejectsInvalidIdentifier(string identifier)
    {
        Assert.False(await _store.EnsureAnimalDatabaseAsync(identifier));
    }

    [Fact]
    public async Task AddAnimalInfo_AppendsRows()
    {
        var status = new ApiAnimalStatus
        {
            Identifier = "mouse-2",
            Name = "Whiskers",
            DcsSendX = 1.0,
            DcsSendY = 2.0,
            DcsSendZ = 3.0,
            TargetYLimit = 4.5
        };

        await _store.AddAnimalInfoAsync(status);
        await _store.AddAnimalInfoAsync(status);

        var factory = new AnimalDbContextFactory(_storage);
        using var db = factory.Create("mouse-2");
        var rows = db.AnimalInfo.ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Whiskers", rows[0].Name);
        Assert.Equal(1.0, rows[0].DcsSendX);
        Assert.Equal(4.5, rows[0].TargetYLimit);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
