using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Data;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoTrainer.Api.Tests.Data.Stores;

public class AnimalDataStoreTests : IDisposable
{
    private const string SessionA = "11111111-1111-4111-8111-111111111111";
    private const string SessionB = "22222222-2222-4222-8222-222222222222";
    private const string BatchA = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

    private readonly string _root;
    private readonly SqliteStorage _storage;
    private readonly AnimalDbContextFactory _factory;
    private readonly TestDeviceDbContextFactory _deviceFactory;
    private readonly DeviceDataStore _deviceStore;
    private readonly AnimalDataStore _store;

    public AnimalDataStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "atanimal-" + Guid.NewGuid().ToString("N"));
        _storage = new SqliteStorage(
            Microsoft.Extensions.Options.Options.Create(new DataOptions { SQLLiteLocation = _root }),
            NullLogger<SqliteStorage>.Instance);
        _storage.EnsureDirectories(out _);
        _factory = new AnimalDbContextFactory(_storage);
        (_deviceFactory, _deviceStore) = TestDeviceStore.CreateMigrated();
        _store = new AnimalDataStore(_factory, _storage, _deviceStore, NullLogger<AnimalDataStore>.Instance);
    }

    private AnimalDbContext Db(string identifier) => _factory.Create(identifier);

    private DeviceDbContext DeviceDb() => _deviceFactory.CreateDbContext();

    private static DateTime At(int second) => new(2026, 7, 13, 10, 0, second, DateTimeKind.Utc);

    private static ApiReachStatus Status(int presented, int consumed, int reaches, int successful) => new()
    {
        PelletsPresented = presented,
        PelletsConsumed = consumed,
        Reaches = reaches,
        SuccessfulReaches = successful
    };

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
    public async Task AddAnimalHistory_AppendsRows()
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

        await _store.AddAnimalHistoryAsync(status);
        await _store.AddAnimalHistoryAsync(status);

        using var db = Db("mouse-2");
        var rows = db.AnimalHistory.ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Whiskers", rows[0].Name);
        Assert.Equal(1.0, rows[0].DcsSendX);
        Assert.Equal(4.5, rows[0].TargetYLimit);
    }

    // Creating an animal's database registers it in the DEVICE database; .animalSelected sets the name there.
    [Fact]
    public async Task AddAnimalHistory_RegistersAnimalInDeviceDb_WithName()
    {
        await _store.AddAnimalHistoryAsync(new ApiAnimalStatus { Identifier = "mouse-3", Name = "Nibbles" });

        using var device = DeviceDb();
        var animal = Assert.Single(device.Animals.ToList());
        Assert.Equal("mouse-3", animal.Identifier);
        Assert.Equal("Nibbles", animal.Name);
    }

    // An animal DB first created by a non-animal event (session/reach) still registers, with an empty name
    // until an .animalSelected arrives.
    [Fact]
    public async Task NonAnimalEvent_CreatingDb_RegistersAnimalWithEmptyName_ThenNameFillsIn()
    {
        await _store.ApplySessionStartedAsync("mouse-4", SessionA, At(1), isAnalysisDeferred: false);

        using (var device = DeviceDb())
        {
            var animal = Assert.Single(device.Animals.ToList());
            Assert.Equal("mouse-4", animal.Identifier);
            Assert.Equal("", animal.Name);
        }

        await _store.AddAnimalHistoryAsync(new ApiAnimalStatus { Identifier = "mouse-4", Name = "Pip" });

        using (var device = DeviceDb())
        {
            var animal = Assert.Single(device.Animals.ToList());   // still one row, now named
            Assert.Equal("Pip", animal.Name);
        }
    }

    [Fact]
    public async Task RepeatedAnimals_RegisterOnceEach()
    {
        await _store.AddAnimalHistoryAsync(new ApiAnimalStatus { Identifier = "a", Name = "A" });
        await _store.AddAnimalHistoryAsync(new ApiAnimalStatus { Identifier = "a", Name = "A" });
        await _store.AddAnimalHistoryAsync(new ApiAnimalStatus { Identifier = "b", Name = "B" });

        using var device = DeviceDb();
        Assert.Equal(2, device.Animals.Count());
    }

    // "Ensure, don't assume": an analysis event for a session/trial we never saw start still lands, and it
    // is the first sight of the batch, so the batch row is created and attached to the trial.
    [Fact]
    public async Task TrialEvent_MidstreamStart_CreatesSessionTrialAndBatch()
    {
        await _store.ApplyTrialEventAsync("m", ApiEventKind.IntertrialDetectionBegin,
            new TrialEventValues(SessionA, 7, BatchA, At(1)));

        using var db = Db("m");

        var session = Assert.Single(db.Sessions.ToList());
        Assert.Equal(SessionA, session.Identifier);
        Assert.Null(session.StartedAt);

        var batch = Assert.Single(db.BatchAnalyses.ToList());
        Assert.Equal(BatchA, batch.Identifier);
        Assert.Equal(session.Id, batch.SessionId);
        Assert.Null(batch.StartedAt);

        var trial = Assert.Single(db.Trials.ToList());
        Assert.Equal(7, trial.Identifier);
        Assert.Equal(session.Id, trial.SessionId);
        Assert.Null(trial.StartedAt);
        Assert.Equal(batch.Id, trial.BatchAnalysisId);
        Assert.Equal(At(1), trial.IntertrialDetectionBeginAt);
    }

    [Fact]
    public async Task Session_StartedThenEnded_PopulatesOneRow()
    {
        await _store.ApplySessionStartedAsync("m", SessionA, At(1), isAnalysisDeferred: true);
        await _store.ApplySessionEndedAsync("m", SessionA, At(9),
            captureTrialCount: 5, analysisTrialCount: 4, failedTrialCount: 1);

        using var db = Db("m");
        var session = Assert.Single(db.Sessions.ToList());

        Assert.Equal(At(1), session.StartedAt);
        Assert.Equal(At(9), session.EndedAt);
        Assert.True(session.IsAnalysisDeferred);
        Assert.Equal(5, session.CaptureTrialCount);
        Assert.Equal(4, session.AnalysisTrialCount);
        Assert.Equal(1, session.FailedTrialCount);
    }

    // trial_id is unique only within a session, so the same id under two sessions is two trials.
    [Fact]
    public async Task Trial_SameIdentifierInTwoSessions_CreatesTwoRows()
    {
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted,
            new TrialEventValues(SessionA, 1, null, At(1)) { Reason = "a" });
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted,
            new TrialEventValues(SessionB, 1, null, At(2)) { Reason = "b" });

        using var db = Db("m");
        var trials = db.Trials.ToList();

        Assert.Equal(2, trials.Count);
        Assert.Equal(2, db.Sessions.Count());
        Assert.Equal(["a", "b"], trials.OrderBy(t => t.StartedAt).Select(t => t.Reason));
    }

    [Fact]
    public async Task TrialSeen_FirstSeenWins()
    {
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialPelletSeen,
            new TrialEventValues(SessionA, 1, null, At(1)));
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialPelletSeen,
            new TrialEventValues(SessionA, 1, null, At(5)));

        using var db = Db("m");
        Assert.Equal(At(1), Assert.Single(db.Trials.ToList()).PelletSeenAt);
    }

    [Fact]
    public async Task PelletShift_LastSeenWins()
    {
        await _store.ApplyTrialEventAsync("m", ApiEventKind.IntertrialPelletShift,
            new TrialEventValues(SessionA, 1, null, At(1)) { PelletShiftJson = "{\"first\":true}" });
        await _store.ApplyTrialEventAsync("m", ApiEventKind.IntertrialPelletShift,
            new TrialEventValues(SessionA, 1, null, At(5)) { PelletShiftJson = "{\"second\":true}" });

        using var db = Db("m");
        Assert.Equal("{\"second\":true}", Assert.Single(db.Trials.ToList()).IntertrialPelletShift);
    }

    // "Record only what we see": an error must not backfill the End it implies.
    [Fact]
    public async Task IntertrialError_DoesNotBackfillEnd()
    {
        await _store.ApplyTrialEventAsync("m", ApiEventKind.IntertrialSegmentationError,
            new TrialEventValues(SessionA, 1, BatchA, At(1)) { Error = "boom" });

        using var db = Db("m");
        var trial = Assert.Single(db.Trials.ToList());

        Assert.Equal("boom", trial.IntertrialSegmentationError);
        Assert.Null(trial.IntertrialSegmentationEndAt);
        Assert.Null(trial.IntertrialSegmentationSaveAt);
    }

    [Fact]
    public async Task ReachEvents_RedeliveryReplacesByTrial_AndSoftDeletesTheOldRows()
    {
        ReachEvent Reach(int init) => new()
        {
            Init = init,
            End = init + 1,
            Max = init,
            Method = ReachEventMethod.RightHand,
            Outcome = ReachEventOutcome.Eaten,
            DelaySincePresented = 0.5
        };

        await _store.ReplaceTrialReachEventsAsync("m", SessionA, 1, BatchA,
            [Reach(1), Reach(2), Reach(3)]);
        await _store.ReplaceTrialReachEventsAsync("m", SessionA, 1, BatchA, [Reach(9)]);

        using var db = Db("m");

        var visible = db.ReachEvents.ToList();
        Assert.Equal(9, Assert.Single(visible).FirstFrame);
        Assert.Equal(2, visible[0].Method);     // right_hand
        Assert.Equal(5, visible[0].Outcome);    // eaten

        var all = db.ReachEvents.IgnoreQueryFilters().ToList();
        Assert.Equal(4, all.Count);
        Assert.Equal(3, all.Count(r => r.DeletedAt != null));
        Assert.All(all.Where(r => r.DeletedAt != null), r => Assert.Contains(r.FirstFrame, new[] { 1, 2, 3 }));
    }

    [Fact]
    public async Task ReachEvents_EmptyListStillReplaces()
    {
        ReachEvent Reach(int init) => new() { Init = init, Method = ReachEventMethod.Tongue, Outcome = ReachEventOutcome.Missed };

        await _store.ReplaceTrialReachEventsAsync("m", SessionA, 1, null, [Reach(1)]);
        await _store.ReplaceTrialReachEventsAsync("m", SessionA, 1, null, []);

        using var db = Db("m");
        Assert.Empty(db.ReachEvents.ToList());
    }

    [Fact]
    public async Task BatchAnalysis_CountComesFromStarted_ThenUpdatedByEnded()
    {
        await _store.ApplyBatchAnalysisStartedAsync("m", SessionA, BatchA, At(1), analysisTrialCount: 5);

        using (var db = Db("m"))
        {
            var started = Assert.Single(db.BatchAnalyses.ToList());
            Assert.Equal(5, started.AnalysisTrialCount);
            Assert.Null(started.FailedTrialCount);
            Assert.Null(started.EndedAt);
        }

        await _store.ApplyBatchAnalysisEndedAsync("m", SessionA, BatchA, At(9),
            analysisTrialCount: 4, failedTrialCount: 1);

        using (var db = Db("m"))
        {
            var ended = Assert.Single(db.BatchAnalyses.ToList());
            Assert.Equal(At(1), ended.StartedAt);
            Assert.Equal(At(9), ended.EndedAt);
            Assert.Equal(4, ended.AnalysisTrialCount);
            Assert.Equal(1, ended.FailedTrialCount);
        }
    }

    [Fact]
    public async Task ReachStatus_WritesOnlyWhenChanged()
    {
        var day = new DateOnly(2026, 7, 13);

        await _store.AddReachStatusIfChangedAsync("m", Status(1, 1, 1, 1), Status(1, 1, 1, 1), day);
        await _store.AddReachStatusIfChangedAsync("m", Status(1, 1, 1, 1), Status(1, 1, 1, 1), day);

        using (var db = Db("m"))
        {
            Assert.Single(db.ReachStatusTotals.ToList());
            Assert.Single(db.ReachStatusDays.ToList());
        }

        await _store.AddReachStatusIfChangedAsync("m", Status(2, 1, 1, 1), Status(2, 1, 1, 1), day);

        using (var db = Db("m"))
        {
            Assert.Equal(2, db.ReachStatusTotals.Count());
            Assert.Equal(2, db.ReachStatusDays.Count());

            var latest = db.ReachStatusDays.OrderByDescending(r => r.Id).First();
            Assert.Equal(day, latest.Day);      // round-trips as a DateOnly, no parsing on read
        }
    }

    // A day rollover to identical counts must still be recorded, or the new day is never seen.
    [Fact]
    public async Task ReachStatus_NewDayWithIdenticalCounts_WritesDayRowOnly()
    {
        await _store.AddReachStatusIfChangedAsync("m", Status(5, 4, 3, 2), Status(0, 0, 0, 0),
            new DateOnly(2026, 7, 13));
        await _store.AddReachStatusIfChangedAsync("m", Status(5, 4, 3, 2), Status(0, 0, 0, 0),
            new DateOnly(2026, 7, 14));

        using var db = Db("m");

        Assert.Single(db.ReachStatusTotals.ToList());          // unchanged totals -> no new row
        Assert.Equal(2, db.ReachStatusDays.Count());           // new day -> new row despite equal counts
        Assert.Equal(new DateOnly(2026, 7, 14),
            db.ReachStatusDays.OrderByDescending(r => r.Id).First().Day);
    }

    public void Dispose()
    {
        _deviceFactory.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }
}
