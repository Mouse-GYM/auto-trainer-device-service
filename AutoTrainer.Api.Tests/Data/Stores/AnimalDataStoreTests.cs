using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Data;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Endpoints;
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

    [Fact]
    public async Task ApplySessionEnded_ReturnsUpdatedScalarSummary()
    {
        await _store.ApplySessionStartedAsync("m", SessionA, At(1), isAnalysisDeferred: true);

        var summary = await _store.ApplySessionEndedAsync("m", SessionA, At(9),
            captureTrialCount: 5, analysisTrialCount: 4, failedTrialCount: 1);

        Assert.NotNull(summary);
        Assert.Equal(SessionA, summary.Identifier);
        Assert.Equal(At(1), summary.StartedAt);
        Assert.Equal(At(9), summary.EndedAt);
        Assert.True(summary.IsAnalysisDeferred);
        Assert.Equal(5, summary.CaptureTrialCount);
        Assert.Equal(4, summary.AnalysisTrialCount);
        Assert.Equal(1, summary.FailedTrialCount);
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
    public async Task IntertrialResult_RedeliveryReplacesByTrial_AndHardDeletesTheOldRows()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, BatchA,
            Response([Reach(1, ReachEventMethod.RightHand, ReachEventOutcome.Eaten),
                      Reach(2, ReachEventMethod.RightHand, ReachEventOutcome.Eaten),
                      Reach(3, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)],
                hand: [Reach(4, ReachEventMethod.LeftHand, ReachEventOutcome.Missed)],
                other: [Reach(5, ReachEventMethod.Tongue, ReachEventOutcome.Stalled)],
                food: 1));

        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, BatchA,
            Response([Reach(9, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)],
                hand: [Reach(8, ReachEventMethod.LeftHand, ReachEventOutcome.Missed)],
                other: [Reach(7, ReachEventMethod.Tongue, ReachEventOutcome.Stalled)],
                food: 2));

        using var db = Db("m");

        // Nothing from the first write survives -- not even as a tombstone. A Remove() creeping back in place
        // of ExecuteDeleteAsync would leave four rows here instead of one.
        var results = db.IntertrialResults.IgnoreQueryFilters().ToList();
        Assert.Equal(2, Assert.Single(results).FoodConsumed);

        var reaches = db.RawReachEvents.IgnoreQueryFilters().ToList();
        Assert.Equal(9, Assert.Single(reaches).FirstFrame);
        Assert.Equal(2, reaches[0].Method);     // right_hand
        Assert.Equal(5, reaches[0].Outcome);    // eaten

        Assert.Equal(8, Assert.Single(db.HandReachEvents.IgnoreQueryFilters().ToList()).FirstFrame);
        Assert.Equal(7, Assert.Single(db.OtherReachEvents.IgnoreQueryFilters().ToList()).FirstFrame);
    }

    [Fact]
    public async Task IntertrialResult_ReplacingASoftDeletedResult_Succeeds()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, null,
            Response([Reach(1, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)]));

        // A delete through the context is a soft delete, and the unique index on TrialId still counts the
        // tombstone -- so the replacement below only works because the store ignores query filters when it
        // looks for the row to hard-delete.
        using (var db = Db("m"))
        {
            db.IntertrialResults.Remove(db.IntertrialResults.Single());
            db.SaveChanges();
            Assert.NotNull(db.IntertrialResults.IgnoreQueryFilters().Single().DeletedAt);
        }

        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, null,
            Response([Reach(9, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)], food: 3));

        using (var db = Db("m"))
        {
            Assert.Equal(3, Assert.Single(db.IntertrialResults.IgnoreQueryFilters().ToList()).FoodConsumed);
            Assert.Equal(9, Assert.Single(db.RawReachEvents.ToList()).FirstFrame);
        }
    }

    [Fact]
    public async Task IntertrialResult_EmptyListsStillReplace()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, null,
            Response([Reach(1, ReachEventMethod.Tongue, ReachEventOutcome.Missed)],
                hand: [Reach(2, ReachEventMethod.Tongue, ReachEventOutcome.Missed)],
                other: [Reach(3, ReachEventMethod.Tongue, ReachEventOutcome.Missed)]));

        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, null, Response([]));

        using var db = Db("m");
        Assert.Single(db.IntertrialResults.ToList());
        Assert.Empty(db.RawReachEvents.ToList());
        Assert.Empty(db.HandReachEvents.ToList());
        Assert.Empty(db.OtherReachEvents.ToList());
    }

    [Fact]
    public async Task IntertrialResult_LinksTrialSessionAndBatch()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 4, BatchA,
            Response([Reach(1, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)]));

        using var db = Db("m");

        var trial = db.Trials.Single();
        var result = db.IntertrialResults.Single();

        Assert.Equal(trial.Id, result.TrialId);
        Assert.Equal(4, trial.Identifier);
        Assert.Equal(db.Sessions.Single().Id, trial.SessionId);
        Assert.Equal(db.BatchAnalyses.Single().Id, trial.BatchAnalysisId);
        Assert.Equal(result.Id, db.RawReachEvents.Single().IntertrialResultId);
        Assert.Equal(trial.Id, db.RawReachEvents.Single().TrialId);
    }

    [Fact]
    public async Task IntertrialResult_WithNoBatch_Persists()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, null,
            Response([Reach(1, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)],
                hand: [Reach(2, ReachEventMethod.LeftHand, ReachEventOutcome.Missed)],
                other: [Reach(3, ReachEventMethod.Tongue, ReachEventOutcome.Stalled)],
                food: 7));

        using var db = Db("m");

        Assert.Empty(db.BatchAnalyses.ToList());
        Assert.Null(db.Trials.Single().BatchAnalysisId);

        var result = db.IntertrialResults.Single();
        Assert.Equal(7, result.FoodConsumed);
        Assert.Equal(1, result.SuccessfulReaches);
        Assert.Equal(1, result.TotalReaches);
        Assert.Equal("[[1,2,3]]", result.RhMaxVpList);
        Assert.Single(db.RawReachEvents.ToList());
        Assert.Single(db.HandReachEvents.ToList());
        Assert.Single(db.OtherReachEvents.ToList());
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

    [Fact]
    public async Task GetReachStatus_ReturnsMostRecentTotal_AndDayRowForDeviceDay()
    {
        var day = new DateOnly(2026, 7, 18);
        await _store.AddReachStatusIfChangedAsync("m", Status(1, 1, 1, 1), Status(1, 1, 1, 1), day);
        await _store.AddReachStatusIfChangedAsync("m", Status(10, 9, 8, 7), Status(3, 2, 1, 0), day);   // newer

        var rs = await _store.GetReachStatusAsync("m", day);

        Assert.Equal(10, rs.Total!.PelletsPresented);   // most recent total
        Assert.Null(rs.Total.Day);
        Assert.Equal(3, rs.Day!.PelletsPresented);      // most recent day row for that day
        Assert.Equal(day, rs.Day.Day);
    }

    [Fact]
    public async Task GetReachStatus_UnknownDeviceDay_OmitsDayPortion()
    {
        await _store.AddReachStatusIfChangedAsync("m", Status(10, 9, 8, 7), Status(3, 2, 1, 0), new DateOnly(2026, 7, 18));

        var rs = await _store.GetReachStatusAsync("m", deviceDay: null);

        Assert.NotNull(rs.Total);
        Assert.Null(rs.Day);   // don't know the device day -> no day portion
    }

    [Fact]
    public async Task GetReachStatus_DeviceDayWithoutRow_OmitsDayPortion()
    {
        await _store.AddReachStatusIfChangedAsync("m", Status(10, 9, 8, 7), Status(3, 2, 1, 0), new DateOnly(2026, 7, 17));

        // Today is the 18th, but the only day row is the 17th's — a stale prior day is not "today".
        var rs = await _store.GetReachStatusAsync("m", new DateOnly(2026, 7, 18));

        Assert.NotNull(rs.Total);
        Assert.Null(rs.Day);
    }

    [Fact]
    public async Task GetReachStatus_NoRows_BothNull()
    {
        await _store.EnsureAnimalDatabaseAsync("m");

        var rs = await _store.GetReachStatusAsync("m", new DateOnly(2026, 7, 18));

        Assert.Null(rs.Total);
        Assert.Null(rs.Day);
    }

    [Fact]
    public async Task GetReachStatus_NoDatabase_BothNull()
    {
        var rs = await _store.GetReachStatusAsync("ghost", new DateOnly(2026, 7, 18));

        Assert.Null(rs.Total);
        Assert.Null(rs.Day);
    }

    // A single-column CountChanged event appends a row that keeps the other counts from the most recent row.
    [Fact]
    public async Task ReachCountChange_Total_CarriesForwardOtherColumns()
    {
        await _store.AddReachStatusIfChangedAsync("m", Status(10, 9, 8, 7), Status(0, 0, 0, 0), null);

        await _store.ApplyReachCountChangeAsync("m", ReachCountScope.Total, ReachCountField.Reaches, 20, day: null);

        using var db = Db("m");
        Assert.Equal(2, db.ReachStatusTotals.Count());
        Assert.Single(db.ReachStatusDays.ToList());   // day table untouched

        var latest = db.ReachStatusTotals.OrderByDescending(r => r.Id).First();
        Assert.Equal(10, latest.PelletsPresented);    // carried forward
        Assert.Equal(9, latest.PelletsConsumed);      // carried forward
        Assert.Equal(20, latest.Reaches);             // updated to the event's Count
        Assert.Equal(7, latest.SuccessfulReaches);    // carried forward
    }

    // On the same day the other columns (and the Day) are carried forward from the most recent day row.
    [Fact]
    public async Task ReachCountChange_Day_SameDay_CarriesForwardOtherColumns()
    {
        var day = new DateOnly(2026, 7, 13);
        await _store.AddReachStatusIfChangedAsync("m", Status(0, 0, 0, 0), Status(3, 2, 1, 0), day);

        await _store.ApplyReachCountChangeAsync("m", ReachCountScope.Day, ReachCountField.SuccessfulReaches, 5, day);

        using var db = Db("m");
        Assert.Equal(2, db.ReachStatusDays.Count());
        Assert.Single(db.ReachStatusTotals.ToList());

        var latest = db.ReachStatusDays.OrderByDescending(r => r.Id).First();
        Assert.Equal(3, latest.PelletsPresented);
        Assert.Equal(2, latest.PelletsConsumed);
        Assert.Equal(1, latest.Reaches);
        Assert.Equal(5, latest.SuccessfulReaches);    // updated
        Assert.Equal(day, latest.Day);                // same day
    }

    // A new attribution day is a rollover: the producer reset its day counters, so the new day's other columns
    // start at zero instead of carrying the previous day's values forward.
    [Fact]
    public async Task ReachCountChange_Day_NewDay_StartsFromZeros_NotPreviousDay()
    {
        var day1 = new DateOnly(2026, 7, 13);
        await _store.AddReachStatusIfChangedAsync("m", Status(0, 0, 0, 0), Status(30, 20, 10, 5), day1);

        // First count of the next day: only PelletsPresented=2, everything else must be 0 (not day1's carryover).
        var day2 = new DateOnly(2026, 7, 14);
        await _store.ApplyReachCountChangeAsync("m", ReachCountScope.Day, ReachCountField.PelletsPresented, 2, day2);

        using var db = Db("m");
        var latest = db.ReachStatusDays.OrderByDescending(r => r.Id).First();
        Assert.Equal(day2, latest.Day);
        Assert.Equal(2, latest.PelletsPresented);
        Assert.Equal(0, latest.PelletsConsumed);   // NOT 20
        Assert.Equal(0, latest.Reaches);           // NOT 10
        Assert.Equal(0, latest.SuccessfulReaches); // NOT 5
    }

    // Generally the value is new, but an unchanged column appends nothing.
    [Fact]
    public async Task ReachCountChange_SameValue_NoNewRow()
    {
        await _store.AddReachStatusIfChangedAsync("m", Status(10, 9, 8, 7), Status(0, 0, 0, 0), null);

        await _store.ApplyReachCountChangeAsync("m", ReachCountScope.Total, ReachCountField.PelletsPresented, 10, day: null);

        using var db = Db("m");
        Assert.Single(db.ReachStatusTotals.ToList());   // 10 == 10 -> nothing appended
    }

    // No prior row for the scope: start from zeros, set only the changed column, touch only that table.
    [Fact]
    public async Task ReachCountChange_NoPriorRow_StartsFromZeros()
    {
        await _store.ApplyReachCountChangeAsync("m", ReachCountScope.Total, ReachCountField.PelletsConsumed, 4, day: null);

        using var db = Db("m");
        var row = Assert.Single(db.ReachStatusTotals.ToList());
        Assert.Equal(0, row.PelletsPresented);
        Assert.Equal(4, row.PelletsConsumed);
        Assert.Equal(0, row.Reaches);
        Assert.Equal(0, row.SuccessfulReaches);
        Assert.Empty(db.ReachStatusDays.ToList());      // only the targeted table gets a row
    }

    // Writes one day-status row for `day` with the given day counts (totals held constant so only day rows move).
    private Task AddDayAsync(DateOnly day, int presented, int consumed, int reaches, int successful) =>
        _store.AddReachStatusIfChangedAsync("m", Status(0, 0, 0, 0),
            Status(presented, consumed, reaches, successful), day);

    [Fact]
    public async Task LoadFiveDay_SumsLatestPerDayOverFiveDayWindow_ExcludingTheSixthDay()
    {
        await AddDayAsync(new DateOnly(2026, 7, 10), 1, 1, 1, 1);   // sixth day back -> excluded
        await AddDayAsync(new DateOnly(2026, 7, 11), 2, 2, 2, 2);
        await AddDayAsync(new DateOnly(2026, 7, 12), 3, 3, 3, 3);
        await AddDayAsync(new DateOnly(2026, 7, 13), 4, 4, 4, 4);
        await AddDayAsync(new DateOnly(2026, 7, 14), 5, 5, 5, 5);
        await AddDayAsync(new DateOnly(2026, 7, 15), 6, 6, 6, 6);   // anchor (most recent)

        var five = await _store.LoadFiveDayReachStatusAsync("m", null);

        Assert.Equal(new DateOnly(2026, 7, 15), five.CurrentDay);
        Assert.Equal(6, five.CurrentDayCounts.PelletsPresented);        // day 15
        Assert.Equal(2 + 3 + 4 + 5, five.PriorFourSum.PelletsPresented); // days 11-14
        Assert.Equal(20, five.Total.PelletsPresented);                  // day 10 (=1) excluded
        Assert.Equal(20, five.Total.SuccessfulReaches);
    }

    [Fact]
    public async Task LoadFiveDay_UsesLatestRowPerDay()
    {
        var d = new DateOnly(2026, 7, 15);
        await AddDayAsync(d, 1, 1, 1, 1);
        await AddDayAsync(d, 9, 8, 7, 6);   // same day, newer row wins

        var five = await _store.LoadFiveDayReachStatusAsync("m", null);

        Assert.Equal(9, five.CurrentDayCounts.PelletsPresented);
        Assert.Equal(9, five.Total.PelletsPresented);
    }

    [Fact]
    public async Task LoadFiveDay_GapWithinWindow_CountsDaysOnBothSides()
    {
        await AddDayAsync(new DateOnly(2026, 7, 11), 2, 0, 0, 0);
        await AddDayAsync(new DateOnly(2026, 7, 12), 4, 0, 0, 0);
        // no row for the 13th
        await AddDayAsync(new DateOnly(2026, 7, 14), 8, 0, 0, 0);
        await AddDayAsync(new DateOnly(2026, 7, 15), 16, 0, 0, 0);   // anchor

        var five = await _store.LoadFiveDayReachStatusAsync("m", null);

        Assert.Equal(16, five.CurrentDayCounts.PelletsPresented);
        Assert.Equal(2 + 4 + 8, five.PriorFourSum.PelletsPresented);   // 13th contributes 0
        Assert.Equal(30, five.Total.PelletsPresented);
    }

    // Rollover: anchoring at a day with no row yields a zero current day and sums the prior four.
    [Fact]
    public async Task LoadFiveDay_AnchorOverride_TreatsAnchorDayWithoutRowAsZero()
    {
        await AddDayAsync(new DateOnly(2026, 7, 14), 5, 0, 0, 0);
        await AddDayAsync(new DateOnly(2026, 7, 15), 7, 0, 0, 0);

        var five = await _store.LoadFiveDayReachStatusAsync("m", new DateOnly(2026, 7, 16));

        Assert.Equal(new DateOnly(2026, 7, 16), five.CurrentDay);
        Assert.Equal(0, five.CurrentDayCounts.PelletsPresented);        // no row for the 16th
        Assert.Equal(5 + 7, five.PriorFourSum.PelletsPresented);        // 14th and 15th now in the prior four
        Assert.Equal(12, five.Total.PelletsPresented);
    }

    [Fact]
    public async Task LoadFiveDay_NoDayRows_ReturnsEmpty()
    {
        await _store.EnsureAnimalDatabaseAsync("m");

        var five = await _store.LoadFiveDayReachStatusAsync("m", null);

        Assert.Null(five.CurrentDay);
        Assert.Equal(0, five.Total.PelletsPresented);
    }

    // ----- Read side (REST query API) --------------------------------------------------------------

    private const string SessionC = "33333333-3333-4333-8333-333333333333";

    private static readonly PageRequest Page = PageRequest.From(null, null);

    private static DateTime Recent => DateTime.UtcNow.AddDays(-1);

    private static ReachEvent Reach(int init, string method, string outcome) => new()
    {
        Init = init,
        End = init + 1,
        Max = init,
        Method = method,
        Outcome = outcome,
        DelaySincePresented = 0.5
    };

    // The scalars default to the reach list's own size so a test that only cares about the rows does not have
    // to state them; RhMaxVpList is a fixed sentinel, since nothing interprets it.
    private static IntertrialResponse Response(List<ReachEvent> reaches, List<ReachEvent>? hand = null,
        List<ReachEvent>? other = null, int food = 0) => new()
        {
            RhMaxVpList = [[1, 2, 3]],
            ReachEvents = reaches,
            HandEvents = hand ?? [],
            OtherEvents = other ?? [],
            FoodConsumed = food,
            SuccessfulReaches = reaches.Count(r => r.Outcome == ReachEventOutcome.Eaten),
            TotalReaches = reaches.Count
        };

    [Fact]
    public async Task Reads_ForUnknownAnimal_AreEmptyOrAbsent_AndCreateNoFile()
    {
        var reaches = await _store.GetRawReachEventsAsync("ghost", Recent, [], [], Page);
        Assert.Empty(reaches.Items);
        Assert.Equal(0, reaches.TotalCount);

        Assert.Empty((await _store.GetSessionsAsync("ghost", Recent, null, Page)).Items);
        Assert.Empty((await _store.GetTrialsAsync("ghost", SessionA, new TrialFilter(null), default, Page)).Items);
        Assert.Empty((await _store.GetBatchesAsync("ghost", SessionA, Page)).Items);
        Assert.Null(await _store.GetSessionAsync("ghost", SessionA, default, default));
        Assert.Null(await _store.GetTrialAsync("ghost", SessionA, 1, expandRightHandReaches: false));
        Assert.Null(await _store.GetAnimalDetailAsync("ghost"));

        Assert.False(File.Exists(_storage.GetAnimalDatabasePath("ghost")));
    }

    // The two reach codes every filtering test below pairs off against each other.
    private static readonly List<ReachEvent> TwoReaches =
        [Reach(1, ReachEventMethod.RightHand, ReachEventOutcome.Eaten),
         Reach(2, ReachEventMethod.LeftHand, ReachEventOutcome.Missed)];

    [Fact]
    public async Task GetRawReachEvents_PagesAndFiltersByCode()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, BatchA, Response(TwoReaches));

        var all = await _store.GetRawReachEventsAsync("m", Recent, [], [], Page);
        Assert.Equal(2, all.TotalCount);

        var rightHand = await _store.GetRawReachEventsAsync("m", Recent, [2], [], Page);   // right_hand = 2
        var rh = Assert.Single(rightHand.Items);
        Assert.Equal(2, rh.Method);
        Assert.Equal(5, rh.Outcome);   // eaten = 5
        Assert.True(rh.TrialId > 0);   // the owning trial's key is on the wire

        var missed = await _store.GetRawReachEventsAsync("m", Recent, [], [2], Page);       // missed = 2
        Assert.Equal(3, Assert.Single(missed.Items).Method);   // left_hand = 3
    }

    [Fact]
    public async Task CountRawReachEvents_MatchesFilters()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, BatchA, Response(TwoReaches));

        Assert.Equal(2, await _store.CountRawReachEventsAsync("m", Recent, [], []));
        Assert.Equal(1, await _store.CountRawReachEventsAsync("m", Recent, [2], []));            // right_hand only
        Assert.Equal(0, await _store.CountRawReachEventsAsync("m", DateTime.UtcNow.AddDays(1), [], []));  // future window
        Assert.Equal(0, await _store.CountRawReachEventsAsync("ghost", Recent, [], []));         // no database
    }

    [Fact]
    public async Task GetHandReachEvents_PagesAndFiltersByCode()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, BatchA, Response([], hand: TwoReaches));

        Assert.Empty((await _store.GetHandReachEventsAsync("ghost", Recent, [], [], Page)).Items);   // no database
        Assert.Equal(2, (await _store.GetHandReachEventsAsync("m", Recent, [], [], Page)).TotalCount);

        var rightHand = await _store.GetHandReachEventsAsync("m", Recent, [2], [], Page);   // right_hand = 2
        var rh = Assert.Single(rightHand.Items);
        Assert.Equal(2, rh.Method);
        Assert.Equal(5, rh.Outcome);   // eaten = 5
        Assert.True(rh.TrialId > 0);   // resolved through the result, since this table has no TrialId column

        var missed = await _store.GetHandReachEventsAsync("m", Recent, [], [2], Page);      // missed = 2
        Assert.Equal(3, Assert.Single(missed.Items).Method);   // left_hand = 3
    }

    [Fact]
    public async Task CountHandReachEvents_MatchesFilters()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, BatchA, Response([], hand: TwoReaches));

        Assert.Equal(2, await _store.CountHandReachEventsAsync("m", Recent, [], []));
        Assert.Equal(1, await _store.CountHandReachEventsAsync("m", Recent, [2], []));
        Assert.Equal(0, await _store.CountHandReachEventsAsync("m", DateTime.UtcNow.AddDays(1), [], []));
        Assert.Equal(0, await _store.CountHandReachEventsAsync("ghost", Recent, [], []));
    }

    [Fact]
    public async Task GetOtherReachEvents_PagesAndFiltersByCode()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, BatchA, Response([], other: TwoReaches));

        Assert.Empty((await _store.GetOtherReachEventsAsync("ghost", Recent, [], [], Page)).Items);   // no database
        Assert.Equal(2, (await _store.GetOtherReachEventsAsync("m", Recent, [], [], Page)).TotalCount);

        var rightHand = await _store.GetOtherReachEventsAsync("m", Recent, [2], [], Page);
        var rh = Assert.Single(rightHand.Items);
        Assert.Equal(2, rh.Method);
        Assert.Equal(5, rh.Outcome);
        Assert.True(rh.TrialId > 0);

        var missed = await _store.GetOtherReachEventsAsync("m", Recent, [], [2], Page);
        Assert.Equal(3, Assert.Single(missed.Items).Method);
    }

    [Fact]
    public async Task CountOtherReachEvents_MatchesFilters()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, BatchA, Response([], other: TwoReaches));

        Assert.Equal(2, await _store.CountOtherReachEventsAsync("m", Recent, [], []));
        Assert.Equal(1, await _store.CountOtherReachEventsAsync("m", Recent, [2], []));
        Assert.Equal(0, await _store.CountOtherReachEventsAsync("m", DateTime.UtcNow.AddDays(1), [], []));
        Assert.Equal(0, await _store.CountOtherReachEventsAsync("ghost", Recent, [], []));
    }

    [Fact]
    public async Task CountSessions_MatchesFilters()
    {
        await _store.ApplySessionStartedAsync("m", SessionA, At(1), isAnalysisDeferred: true);
        await _store.ApplySessionStartedAsync("m", SessionB, At(2), isAnalysisDeferred: false);

        Assert.Equal(2, await _store.CountSessionsAsync("m", since: null, isAnalysisDeferred: null));
        Assert.Equal(1, await _store.CountSessionsAsync("m", null, isAnalysisDeferred: true));
        Assert.Equal(0, await _store.CountSessionsAsync("m", DateTime.UtcNow.AddDays(1), null));   // future window
        Assert.Equal(0, await _store.CountSessionsAsync("ghost", null, null));                     // no database
    }

    [Fact]
    public async Task CountTrials_MatchesFilters()
    {
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialEnded,
            new TrialEventValues(SessionA, 1, null, At(1)) { Result = CaptureAnalysisResult.AnalysisSucceeded });
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialEnded,
            new TrialEventValues(SessionA, 2, null, At(2)) { Result = CaptureAnalysisResult.CaptureOnly });

        Assert.Equal(2, await _store.CountTrialsAsync("m", SessionA, new TrialFilter(null)));
        Assert.Equal(1, await _store.CountTrialsAsync("m", SessionA, new TrialFilter(true)));   // analysis performed
        Assert.Equal(0, await _store.CountTrialsAsync("m", "no-such-session", new TrialFilter(null)));
    }

    [Fact]
    public async Task GetSessions_FiltersDeferred_AndWindowKeepsMidstreamSession()
    {
        await _store.ApplySessionStartedAsync("m", SessionA, At(1), isAnalysisDeferred: true);
        await _store.ApplySessionStartedAsync("m", SessionB, At(2), isAnalysisDeferred: false);
        // A session first seen mid-stream: created by a trial event, with no StartedAt.
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialPelletPresented,
            new TrialEventValues(SessionC, 1, null, At(3)));

        var all = await _store.GetSessionsAsync("m", Recent, null, Page);
        Assert.Equal(3, all.TotalCount);   // includes the null-StartedAt session (window is on CreatedAt)

        var deferred = await _store.GetSessionsAsync("m", Recent, true, Page);
        Assert.Equal(SessionA, Assert.Single(deferred.Items).Identifier);
    }

    [Fact]
    public async Task GetSessions_NullWindow_ReturnsAll_WindowIsAppliedWhenPresent()
    {
        await _store.ApplySessionStartedAsync("m", SessionA, At(1), isAnalysisDeferred: false);
        await _store.ApplySessionStartedAsync("m", SessionB, At(2), isAnalysisDeferred: false);

        // No window -> every session for the animal; total is just the session count.
        var all = await _store.GetSessionsAsync("m", since: null, isAnalysisDeferred: null, Page);
        Assert.Equal(2, all.TotalCount);

        // A future window excludes everything, proving `since` is genuinely applied when supplied.
        var future = await _store.GetSessionsAsync("m", DateTime.UtcNow.AddDays(1), null, Page);
        Assert.Equal(0, future.TotalCount);
    }

    [Fact]
    public async Task GetSession_HonorsExpandDepth()
    {
        await _store.ApplySessionStartedAsync("m", SessionA, At(1), isAnalysisDeferred: true);
        await _store.ApplyBatchAnalysisStartedAsync("m", SessionA, BatchA, At(1), analysisTrialCount: 3);
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted,
            new TrialEventValues(SessionA, 7, BatchA, At(2)) { Reason = "r" });
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 7, BatchA,
            Response([Reach(1, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)],
                hand: [Reach(2, ReachEventMethod.RightHand, ReachEventOutcome.Grabbed)]));

        var bare = await _store.GetSessionAsync("m", SessionA, default, default);
        Assert.NotNull(bare);
        Assert.Null(bare.Trials);
        Assert.Null(bare.Batches);

        var withTrials = await _store.GetSessionAsync("m", SessionA, new SessionExpand(true, false, false), default);
        var trial = Assert.Single(withTrials!.Trials!);
        Assert.Equal(7, trial.Identifier);
        Assert.Equal(BatchA, trial.BatchId);
        Assert.Equal(1, trial.RawReachEventCount);
        Assert.Null(trial.RightHandReaches);   // trials alone stops at the counts

        var withReaches = await _store.GetSessionAsync("m", SessionA, new SessionExpand(true, true, false), default);
        Assert.Equal(2, Assert.Single(Assert.Single(withReaches!.Trials!).RightHandReaches!).FirstFrame);

        var withBatches = await _store.GetSessionAsync("m", SessionA, new SessionExpand(false, false, true), default);
        Assert.Single(withBatches!.Batches!);

        Assert.Null(await _store.GetSessionAsync("m", SessionB, default, default));   // missing session
    }

    [Fact]
    public async Task GetTrials_FiltersByAnalysisPerformed()
    {
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialEnded,
            new TrialEventValues(SessionA, 1, null, At(1)) { Result = CaptureAnalysisResult.AnalysisSucceeded });
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, null,
            Response([Reach(1, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)]));

        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialEnded,
            new TrialEventValues(SessionA, 2, null, At(2)) { Result = CaptureAnalysisResult.CaptureOnly });
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 2, null,
            Response([Reach(1, ReachEventMethod.RightHand, ReachEventOutcome.Missed)]));

        var performed = await _store.GetTrialsAsync("m", SessionA, new TrialFilter(true), default, Page);
        Assert.Equal(1, Assert.Single(performed.Items).Identifier);

        var notPerformed = await _store.GetTrialsAsync("m", SessionA, new TrialFilter(false), default, Page);
        Assert.Equal(2, Assert.Single(notPerformed.Items).Identifier);
    }

    [Fact]
    public async Task TrialCounts_ExcludeSoftDeletedReaches()
    {
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted,
            new TrialEventValues(SessionA, 1, null, At(1)));
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, null,
            Response([Reach(1, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)],
                hand: [Reach(2, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)]));

        // Delete through the context, which AppDbContext turns into a soft delete. (A redelivery would not do:
        // it hard-deletes, so the rows would be gone rather than filtered, and this would pass even if the
        // query filter did not reach inside the count subqueries.)
        using (var db = Db("m"))
        {
            db.RawReachEvents.Remove(db.RawReachEvents.Single());
            db.HandReachEvents.Remove(db.HandReachEvents.Single());
            db.SaveChanges();
            Assert.NotNull(db.RawReachEvents.IgnoreQueryFilters().Single().DeletedAt);
        }

        var trial = await _store.GetTrialAsync("m", SessionA, 1, expandRightHandReaches: false);

        Assert.Equal(0, trial!.RawReachEventCount);
        Assert.Equal(0, trial.RightHandReachEventCount);
        Assert.Equal(0, trial.HandReachEventCount);
    }

    [Fact]
    public async Task GetTrials_SortByIdentifier_OrdersAscDesc_AndPaginates()
    {
        // Started-at order (3,1,2) differs from identifier order, so the default sort and the identifier
        // sorts are all distinguishable.
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted, new TrialEventValues(SessionA, 3, null, At(1)));
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted, new TrialEventValues(SessionA, 1, null, At(2)));
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted, new TrialEventValues(SessionA, 2, null, At(3)));

        var filter = new TrialFilter(null);

        // Default (no sort): newest-started first.
        var def = await _store.GetTrialsAsync("m", SessionA, filter, default, Page);
        Assert.Equal(new[] { 2, 1, 3 }, def.Items.Select(t => t.Identifier).ToArray());

        var asc = await _store.GetTrialsAsync("m", SessionA, filter, SortRequest.From("identifier"), Page);
        Assert.Equal(new[] { 1, 2, 3 }, asc.Items.Select(t => t.Identifier).ToArray());

        var desc = await _store.GetTrialsAsync("m", SessionA, filter, SortRequest.From("-identifier"), Page);
        Assert.Equal(new[] { 3, 2, 1 }, desc.Items.Select(t => t.Identifier).ToArray());

        // Pagination walks the requested (ascending identifier) order across pages.
        var page1 = await _store.GetTrialsAsync("m", SessionA, filter, SortRequest.From("identifier"), PageRequest.From(1, 2));
        Assert.Equal(new[] { 1, 2 }, page1.Items.Select(t => t.Identifier).ToArray());
        Assert.Equal(3, page1.TotalCount);

        var page2 = await _store.GetTrialsAsync("m", SessionA, filter, SortRequest.From("identifier"), PageRequest.From(2, 2));
        Assert.Equal(new[] { 3 }, page2.Items.Select(t => t.Identifier).ToArray());
    }

    [Fact]
    public async Task GetSession_ExpandTrials_HonorsSort()
    {
        await _store.ApplySessionStartedAsync("m", SessionA, At(1), isAnalysisDeferred: false);
        // Started-at order (3,1,2) differs from identifier order so default vs identifier sort are distinguishable.
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted, new TrialEventValues(SessionA, 3, null, At(1)));
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted, new TrialEventValues(SessionA, 1, null, At(2)));
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted, new TrialEventValues(SessionA, 2, null, At(3)));

        var expandTrials = new SessionExpand(true, false, false);

        // Default (no sort): newest-started first.
        var def = await _store.GetSessionAsync("m", SessionA, expandTrials, default);
        Assert.Equal(new[] { 2, 1, 3 }, def!.Trials!.Select(t => t.Identifier).ToArray());

        var asc = await _store.GetSessionAsync("m", SessionA, expandTrials, SortRequest.From("identifier"));
        Assert.Equal(new[] { 1, 2, 3 }, asc!.Trials!.Select(t => t.Identifier).ToArray());

        var desc = await _store.GetSessionAsync("m", SessionA, expandTrials, SortRequest.From("-identifier"));
        Assert.Equal(new[] { 3, 2, 1 }, desc!.Trials!.Select(t => t.Identifier).ToArray());
    }

    [Fact]
    public async Task GetTrial_ReturnsTheTrial_AndNullForUnknown()
    {
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted,
            new TrialEventValues(SessionA, 5, null, At(1)) { Reason = "hi" });
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 5, null,
            Response([Reach(1, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)]));

        var trial = await _store.GetTrialAsync("m", SessionA, 5, expandRightHandReaches: false);
        Assert.NotNull(trial);
        Assert.Equal("hi", trial.Reason);
        Assert.Equal(1, trial.RawReachEventCount);

        Assert.Null(await _store.GetTrialAsync("m", SessionA, 999, expandRightHandReaches: false));
    }

    [Fact]
    public async Task GetTrial_CarriesIntertrialResultScalars()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 5, null,
            Response([Reach(1, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)],
                hand: [Reach(2, ReachEventMethod.LeftHand, ReachEventOutcome.Missed)],
                other: [Reach(3, ReachEventMethod.Tongue, ReachEventOutcome.Stalled),
                        Reach(4, ReachEventMethod.Tongue, ReachEventOutcome.Stalled)],
                food: 9));

        // The scalars ride on the unexpanded projection too, so a trials list carries them.
        var trial = await _store.GetTrialAsync("m", SessionA, 5, expandRightHandReaches: false);
        Assert.Equal(9, trial!.FoodConsumed);
        Assert.Equal(1, trial.SuccessfulReaches);
        Assert.Equal(1, trial.TotalReaches);
        Assert.Equal("[[1,2,3]]", trial.RhMaxVpList);
        Assert.Equal(1, trial.RawReachEventCount);
        Assert.Equal(1, trial.HandReachEventCount);
        Assert.Equal(2, trial.OtherReachEventCount);
    }

    // The count that answers "how many reaches did this trial have": hand events with method right_hand, not
    // the raw reach rows and not every hand event. All three differ here so nothing can be counting the wrong
    // table and passing by coincidence.
    [Fact]
    public async Task GetTrial_RightHandReachEventCount_CountsRightHandHandEventsOnly()
    {
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 5, null,
            Response(
                [Reach(1, ReachEventMethod.RightHand, ReachEventOutcome.Eaten),
                 Reach(2, ReachEventMethod.RightHand, ReachEventOutcome.Missed),
                 Reach(3, ReachEventMethod.LeftHand, ReachEventOutcome.Missed)],
                hand:
                [Reach(4, ReachEventMethod.RightHand, ReachEventOutcome.Grabbed),
                 Reach(5, ReachEventMethod.RightHand, ReachEventOutcome.Dropped),
                 Reach(6, ReachEventMethod.RightHand, ReachEventOutcome.Missed),
                 Reach(7, ReachEventMethod.LeftHand, ReachEventOutcome.Missed),
                 Reach(8, ReachEventMethod.Tongue, ReachEventOutcome.None)],
                other: [Reach(9, ReachEventMethod.Other, ReachEventOutcome.None)]));

        var trial = await _store.GetTrialAsync("m", SessionA, 5, expandRightHandReaches: false);

        Assert.Equal(3, trial!.RightHandReachEventCount);   // the three right-hand hand events
        Assert.Equal(5, trial.HandReachEventCount);         // every hand event, whatever the method
        Assert.Equal(3, trial.RawReachEventCount);          // the analysis's own reach rows
        Assert.Equal(1, trial.OtherReachEventCount);
        Assert.Null(trial.RightHandReaches);                // unexpanded

        // Expanded, the list is exactly the rows the count counted -- same table, same method filter.
        var expanded = await _store.GetTrialAsync("m", SessionA, 5, expandRightHandReaches: true);

        Assert.Equal([4, 5, 6], expanded!.RightHandReaches!.Select(r => r.FirstFrame));
        Assert.All(expanded.RightHandReaches!, r => Assert.Equal(ReachEventMethod.RightHandCode, r.Method));
        Assert.Equal(expanded.RightHandReachEventCount, expanded.RightHandReaches!.Count);

        // Hand events have no TrialId column; it is resolved through the intertrial result.
        Assert.All(expanded.RightHandReaches!, r => Assert.True(r.TrialId > 0));
    }

    // Expanded means non-null even when there is nothing to return -- null is reserved for "not expanded".
    [Fact]
    public async Task GetTrial_ExpandRightHandReaches_TrialWithNoResult_ReturnsEmptyListNotNull()
    {
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted,
            new TrialEventValues(SessionA, 5, null, At(1)));

        var trial = await _store.GetTrialAsync("m", SessionA, 5, expandRightHandReaches: true);

        Assert.Empty(trial!.RightHandReaches!);
    }

    // The same normalisation has to hold at the session call site.
    [Fact]
    public async Task GetSession_ExpandTrialsRightHandReaches_TrialWithNoResult_ReturnsEmptyList()
    {
        await _store.ApplySessionStartedAsync("m", SessionA, At(1), isAnalysisDeferred: false);
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted,
            new TrialEventValues(SessionA, 5, null, At(1)));

        var session = await _store.GetSessionAsync("m", SessionA, new SessionExpand(true, true, false), default);

        Assert.Empty(Assert.Single(session!.Trials!).RightHandReaches!);
    }

    // Two trials in one session: the second round trip keys rows by trial, so a mix-up would show here.
    [Fact]
    public async Task GetSession_ExpandTrialsRightHandReaches_KeepsRowsWithTheirOwnTrial()
    {
        await _store.ApplySessionStartedAsync("m", SessionA, At(1), isAnalysisDeferred: false);
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 1, null,
            Response([], hand: [Reach(10, ReachEventMethod.RightHand, ReachEventOutcome.Eaten)]));
        await _store.ReplaceIntertrialResultAsync("m", SessionA, 2, null,
            Response([], hand:
                [Reach(20, ReachEventMethod.RightHand, ReachEventOutcome.Missed),
                 Reach(21, ReachEventMethod.LeftHand, ReachEventOutcome.Missed)]));

        var session = await _store.GetSessionAsync("m", SessionA,
            new SessionExpand(true, true, false), SortRequest.From("identifier"));

        var trials = session!.Trials!;
        Assert.Equal([10], trials[0].RightHandReaches!.Select(r => r.FirstFrame));
        Assert.Equal([20], trials[1].RightHandReaches!.Select(r => r.FirstFrame));
    }

    [Fact]
    public async Task GetTrial_WithNoResult_ScalarsAreNull_AndCountsZero()
    {
        await _store.ApplyTrialEventAsync("m", ApiEventKind.TrialStarted,
            new TrialEventValues(SessionA, 5, null, At(1)));

        var trial = await _store.GetTrialAsync("m", SessionA, 5, expandRightHandReaches: false);

        Assert.Null(trial!.FoodConsumed);
        Assert.Null(trial.SuccessfulReaches);
        Assert.Null(trial.TotalReaches);
        Assert.Null(trial.RhMaxVpList);
        Assert.Equal(0, trial.RightHandReachEventCount);
        Assert.Equal(0, trial.RawReachEventCount);
        Assert.Equal(0, trial.HandReachEventCount);
        Assert.Equal(0, trial.OtherReachEventCount);
    }

    [Fact]
    public async Task GetBatches_ListsForSession_AndEmptyForMissingSession()
    {
        await _store.ApplyBatchAnalysisStartedAsync("m", SessionA, BatchA, At(1), analysisTrialCount: 5);

        var batches = await _store.GetBatchesAsync("m", SessionA, Page);
        var b = Assert.Single(batches.Items);
        Assert.Equal(BatchA, b.Identifier);
        Assert.Equal(5, b.AnalysisTrialCount);

        Assert.Empty((await _store.GetBatchesAsync("m", SessionB, Page)).Items);
    }

    [Fact]
    public async Task GetAnimalDetail_ReturnsLatestIdentityAndSnapshots()
    {
        await _store.AddAnimalHistoryAsync(new ApiAnimalStatus
        {
            Identifier = "m",
            Name = "Whiskers",
            DcsSendX = 1.0,
            DcsSendY = 2.0,
            DcsSendZ = 3.0,
            TargetYLimit = 4.5
        });
        await _store.AddReachStatusIfChangedAsync("m", Status(10, 9, 8, 7), Status(2, 1, 1, 0),
            new DateOnly(2026, 7, 13));

        var detail = await _store.GetAnimalDetailAsync("m");

        Assert.NotNull(detail);
        Assert.Equal("Whiskers", detail.Name);
        Assert.Equal(1.0, detail.DcsSendX);
        Assert.Equal(4.5, detail.TargetYLimit);
        Assert.Equal(10, detail.ReachStatusTotal!.PelletsPresented);
        Assert.Null(detail.ReachStatusTotal.Day);
        Assert.Equal(new DateOnly(2026, 7, 13), detail.ReachStatusDay!.Day);
        // One day in the window, so the 5-day equals it; it's an aggregate, so its Day is null.
        Assert.Equal(2, detail.ReachStatus5Day!.PelletsPresented);
        Assert.Null(detail.ReachStatus5Day.Day);
    }

    [Fact]
    public async Task GetAnimalDetail_FiveDay_AggregatesAcrossDays()
    {
        await _store.AddAnimalHistoryAsync(new ApiAnimalStatus { Identifier = "m", Name = "W" });
        await _store.AddReachStatusIfChangedAsync("m", Status(0, 0, 0, 0), Status(3, 0, 0, 0), new DateOnly(2026, 7, 14));
        await _store.AddReachStatusIfChangedAsync("m", Status(0, 0, 0, 0), Status(5, 0, 0, 0), new DateOnly(2026, 7, 15));

        var detail = await _store.GetAnimalDetailAsync("m");

        Assert.Equal(8, detail!.ReachStatus5Day!.PelletsPresented);   // 3 + 5 across the two days
        Assert.Null(detail.ReachStatus5Day.Day);
    }

    [Fact]
    public async Task GetAnimalDetail_NoDayRows_FiveDayIsNull()
    {
        await _store.AddAnimalHistoryAsync(new ApiAnimalStatus { Identifier = "m", Name = "W" });

        var detail = await _store.GetAnimalDetailAsync("m");

        Assert.NotNull(detail);
        Assert.Null(detail.ReachStatusDay);
        Assert.Null(detail.ReachStatus5Day);
    }

    // Sets the TrainerNotes column directly and appends one behavior note, which is what the detail read joins.
    private async Task SetRegistryNotesAsync(string identifier, string trainer, string behavior)
    {
        using var db = DeviceDb();
        var row = db.Animals.Single(a => a.Identifier == identifier);
        row.TrainerNotes = trainer;
        db.BehaviorNotes.Add(new AutoTrainer.Api.Data.Entities.BehaviorNote { AnimalId = row.Id, Body = behavior });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAnimalDetail_CarriesNotesFromDeviceRegistry()
    {
        // AddAnimalHistoryAsync creates the registry row via SetAnimalNameAsync.
        await _store.AddAnimalHistoryAsync(new ApiAnimalStatus { Identifier = "m", Name = "Whiskers" });
        await SetRegistryNotesAsync("m", "prefers left paw", "skittish after 16:00");

        var detail = await _store.GetAnimalDetailAsync("m");

        Assert.Equal("prefers left paw", detail!.TrainerNotes);
        Assert.Equal("skittish after 16:00", detail.BehaviorNote!.Body);
    }

    [Fact]
    public async Task GetAnimalDetail_NotesOnlyRegistryRow_StillReturnsNull()
    {
        // The animal's own database exists but holds no history or reach-status rows.
        Assert.True(await _store.EnsureAnimalDatabaseAsync("m"));
        await SetRegistryNotesAsync("m", "prefers left paw", "skittish after 16:00");

        // The cross-store notes read sits after the presence check, so notes alone cannot resurrect a detail.
        Assert.Null(await _store.GetAnimalDetailAsync("m"));
    }

    [Fact]
    public async Task GetAnimalDetail_NoRegistryRow_NotesAreEmpty()
    {
        await _store.AddAnimalHistoryAsync(new ApiAnimalStatus { Identifier = "m", Name = "W" });

        using (var db = DeviceDb())
        {
            db.Animals.RemoveRange(db.Animals.ToList());
            await db.SaveChangesAsync();
        }

        var detail = await _store.GetAnimalDetailAsync("m");

        Assert.NotNull(detail);
        Assert.Equal("", detail.TrainerNotes);
        Assert.Null(detail.BehaviorNote);
    }

    public void Dispose()
    {
        _deviceFactory.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }
}
