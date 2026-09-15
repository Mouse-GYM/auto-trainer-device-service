using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Data;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoTrainer.Api.Tests.Data.Stores;

// The ensure-session/trial/batch upsert must persist in a SINGLE SaveChanges, not one per row. These tests
// count the actual round trips rather than trusting the shape of the code.
public class AnimalDataStoreSaveCountTests : IDisposable
{
    private const string SessionA = "11111111-1111-4111-8111-111111111111";
    private const string BatchA = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

    private readonly string _root;
    private readonly SqliteStorage _storage;
    private readonly CountingFactory _factory;
    private readonly TestDeviceDbContextFactory _deviceFactory;
    private readonly AnimalDataStore _store;

    public AnimalDataStoreSaveCountTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "atsave-" + Guid.NewGuid().ToString("N"));
        _storage = new SqliteStorage(
            Microsoft.Extensions.Options.Options.Create(new DataOptions { SQLLiteLocation = _root }),
            NullLogger<SqliteStorage>.Instance);
        _storage.EnsureDirectories(out _);
        _factory = new CountingFactory(_storage);
        // Device writes go to a separate context, so they never touch the animal-side SaveCounter below.
        (_deviceFactory, var deviceStore) = TestDeviceStore.CreateMigrated();
        _store = new AnimalDataStore(_factory, _storage, deviceStore, NullLogger<AnimalDataStore>.Instance);
    }

    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Count { get; private set; }

        // Forces the insert half of a write to fail, so a test can prove what the failure leaves behind.
        public bool ThrowOnSave { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            Count++;

            if (ThrowOnSave)
                throw new InvalidOperationException("save rejected by the test interceptor");

            return base.SavingChangesAsync(eventData, result, ct);
        }

        public void Reset() => Count = 0;
    }

    private sealed class CountingFactory(ISqliteStorage storage) : IAnimalDbContextFactory
    {
        public SaveCounter Counter { get; } = new();

        public AnimalDbContext Create(string identifier)
        {
            var options = new DbContextOptionsBuilder<AnimalDbContext>()
                .UseSqlite(storage.GetAnimalConnectionString(identifier))
                .AddInterceptors(Counter)
                .Options;

            return new AnimalDbContext(options);
        }
    }

    // The worst case: one event creates the session, the trial and the batch, all three brand new.
    [Fact]
    public async Task TrialEvent_CreatingSessionTrialAndBatch_SavesOnce()
    {
        await _store.EnsureAnimalDatabaseAsync("m");   // migration runs before we start counting
        _factory.Counter.Reset();

        await _store.ApplyTrialEventAsync("m", ApiEventKind.IntertrialDetectionBegin,
            new TrialEventValues(SessionA, 7, BatchA, DateTime.UtcNow));

        Assert.Equal(1, _factory.Counter.Count);

        // ...and it still wrote everything, with the relationships resolved.
        await using var db = _factory.Create("m");
        var trial = Assert.Single(db.Trials.ToList());
        var session = Assert.Single(db.Sessions.ToList());
        var batch = Assert.Single(db.BatchAnalyses.ToList());
        Assert.Equal(session.Id, trial.SessionId);
        Assert.Equal(batch.Id, trial.BatchAnalysisId);
        Assert.Equal(session.Id, batch.SessionId);
    }

    [Fact]
    public async Task TrialEvent_OnExistingRows_SavesOnce()
    {
        await _store.ApplyTrialEventAsync("m", ApiEventKind.IntertrialDetectionBegin,
            new TrialEventValues(SessionA, 7, BatchA, DateTime.UtcNow));
        _factory.Counter.Reset();

        await _store.ApplyTrialEventAsync("m", ApiEventKind.IntertrialDetectionEnd,
            new TrialEventValues(SessionA, 7, BatchA, DateTime.UtcNow));

        Assert.Equal(1, _factory.Counter.Count);
    }

    [Fact]
    public async Task IntertrialResult_CreatingSessionTrialAndBatch_SavesOnce()
    {
        await _store.EnsureAnimalDatabaseAsync("m");
        _factory.Counter.Reset();

        await _store.ReplaceIntertrialResultAsync("m", SessionA, 7, BatchA, Response());

        Assert.Equal(1, _factory.Counter.Count);

        await using var db = _factory.Create("m");
        var trial = Assert.Single(db.Trials.ToList());
        var result = Assert.Single(db.IntertrialResults.ToList());
        var reach = Assert.Single(db.RawReachEvents.ToList());
        Assert.Equal(trial.Id, result.TrialId);
        Assert.Equal(trial.Id, reach.TrialId);
        Assert.Equal(result.Id, reach.IntertrialResultId);
        Assert.Equal(result.Id, Assert.Single(db.HandReachEvents.ToList()).IntertrialResultId);
        Assert.Equal(result.Id, Assert.Single(db.OtherReachEvents.ToList()).IntertrialResultId);
        Assert.NotEqual(0, trial.BatchAnalysisId);
    }

    [Fact]
    public async Task IntertrialResult_ReplacementRollsBackWhenTheInsertFails()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 7, BatchA, Response(food: 1));

        // The replacement hard-deletes the durable result and its children, then inserts. Make the insert
        // fail: without the explicit transaction around the pair, the deletes would already have committed
        // and the trial would be left with no result at all.
        _factory.Counter.ThrowOnSave = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.ReplaceIntertrialResultAsync("m", SessionA, 7, BatchA, Response(food: 2)));
        _factory.Counter.ThrowOnSave = false;

        await using var db = _factory.Create("m");
        Assert.Equal(1, Assert.Single(db.IntertrialResults.ToList()).FoodConsumed);
        Assert.Single(db.RawReachEvents.ToList());
        Assert.Single(db.HandReachEvents.ToList());
        Assert.Single(db.OtherReachEvents.ToList());
    }

    private static IntertrialResponse Response(int food = 0) => new()
    {
        ReachEvents = [Reach(1)],
        HandEvents = [Reach(2)],
        OtherEvents = [Reach(3)],
        FoodConsumed = food
    };

    private static ReachEvent Reach(int init) =>
        new() { Init = init, Method = ReachEventMethod.RightHand, Outcome = ReachEventOutcome.Eaten };

    [Fact]
    public async Task BatchAnalysisStarted_CreatingSessionAndBatch_SavesOnce()
    {
        await _store.EnsureAnimalDatabaseAsync("m");
        _factory.Counter.Reset();

        await _store.ApplyBatchAnalysisStartedAsync("m", SessionA, BatchA, DateTime.UtcNow, 5);

        Assert.Equal(1, _factory.Counter.Count);
    }

    public void Dispose()
    {
        _deviceFactory.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }
}
