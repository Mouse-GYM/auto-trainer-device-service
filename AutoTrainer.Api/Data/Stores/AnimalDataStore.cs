using System.Collections.Concurrent;
using AutoTrainer.Api.ApiTypes;
using Entities = AutoTrainer.Api.Data.Entities;

namespace AutoTrainer.Api.Data.Stores;

// Keys plus every scalar a trial-scoped event can carry. The store owns the kind -> column map.
public sealed record TrialEventValues(string SessionId, int TrialId, string? BatchId, DateTime When)
{
    public string? Reason { get; init; }
    public string? Result { get; init; }
    public string? Error { get; init; }
    public string? Location { get; init; }
    public string? PelletShiftJson { get; init; }
}

public interface IAnimalDataStore
{
    Task<bool> EnsureAnimalDatabaseAsync(string identifier, CancellationToken ct = default);

    Task AddAnimalHistoryAsync(ApiAnimalStatus status, CancellationToken ct = default);

    Task ApplySessionStartedAsync(string identifier, string sessionId, DateTime when,
        bool isAnalysisDeferred, CancellationToken ct = default);

    Task ApplySessionEndedAsync(string identifier, string sessionId, DateTime when,
        int captureTrialCount, int analysisTrialCount, int failedTrialCount, CancellationToken ct = default);

    Task ApplyBatchAnalysisStartedAsync(string identifier, string sessionId, string batchId,
        DateTime when, int analysisTrialCount, CancellationToken ct = default);

    Task ApplyBatchAnalysisEndedAsync(string identifier, string sessionId, string batchId,
        DateTime when, int analysisTrialCount, int failedTrialCount, CancellationToken ct = default);

    Task ApplyTrialEventAsync(string identifier, ApiEventKind kind, TrialEventValues values,
        CancellationToken ct = default);

    Task ReplaceTrialReachEventsAsync(string identifier, string sessionId, int trialId, string? batchId,
        IReadOnlyCollection<ReachEvent> reachEvents, CancellationToken ct = default);

    Task AddReachStatusIfChangedAsync(string identifier, ApiReachStatus total, ApiReachStatus day,
        DateOnly? dayValue, CancellationToken ct = default);

    Task<IReadOnlyList<Entities.ReachEvent>> GetReachEventHistoryAsync(string identifier, DateTime since,
        CancellationToken ct = default);
}

public partial class AnimalDataStore(IAnimalDbContextFactory factory, ISqliteStorage storage, IDeviceDataStore deviceStore, ILogger<AnimalDataStore> logger) : IAnimalDataStore
{
    private readonly ILogger<AnimalDataStore> _logger = logger;

    private readonly ConcurrentDictionary<string, bool> _initialized = new();

    // Last-written reach status per animal, so the repeated systemStatus stream does not hit the database on
    // every message. Purely a performance cache — it tracks nothing but the newest row's values.
    private readonly ConcurrentDictionary<string, ReachStatusCache> _reachStatus = new();

    public async Task<bool> EnsureAnimalDatabaseAsync(string identifier, CancellationToken ct = default)
    {
        if (!SqliteStorage.IsValidIdentifier(identifier))
        {
            LogRejectedIdentifier(identifier);
            return false;
        }

        if (_initialized.ContainsKey(identifier))
            return true;

        // "Never seen before" = the file did not exist prior to this call. On a restart the file is already
        // there (just not in the in-process memo), so this stays false and the device row is not re-created.
        var isNewAnimal = !File.Exists(storage.GetAnimalDatabasePath(identifier));

        await using var db = factory.Create(identifier);
        await db.Database.MigrateAsync(ct);
        _initialized[identifier] = true;

        // First-ever creation of this animal's database also registers it in the device-level Animal table.
        if (isNewAnimal)
            await deviceStore.RegisterAnimalAsync(identifier, ct);

        LogAnimalDatabaseReady(identifier);
        return true;
    }

    public async Task AddAnimalHistoryAsync(ApiAnimalStatus status, CancellationToken ct = default)
    {
        if (!await EnsureAnimalDatabaseAsync(status.Identifier, ct))
            return;

        await using var db = factory.Create(status.Identifier);

        db.AnimalHistory.Add(new Entities.AnimalHistory
        {
            Identifier = status.Identifier,
            Name = status.Name ?? "",
            DcsSendX = status.DcsSendX,
            DcsSendY = status.DcsSendY,
            DcsSendZ = status.DcsSendZ,
            TargetYLimit = status.TargetYLimit
        });

        await db.SaveChangesAsync(ct);

        // Keep the device-level registry's name current (from .animalSelected/.animalUpdated).
        await deviceStore.SetAnimalNameAsync(status.Identifier, status.Name ?? "", ct);
    }

    // --- Session ------------------------------------------------------------------------------------

    public async Task ApplySessionStartedAsync(string identifier, string sessionId, DateTime when,
        bool isAnalysisDeferred, CancellationToken ct = default)
    {
        if (!await EnsureAnimalDatabaseAsync(identifier, ct))
            return;

        await using var db = factory.Create(identifier);

        var session = await EnsureSessionAsync(db, sessionId, ct);
        session.StartedAt = when;
        session.IsAnalysisDeferred = isAnalysisDeferred;

        await db.SaveChangesAsync(ct);
    }

    public async Task ApplySessionEndedAsync(string identifier, string sessionId, DateTime when,
        int captureTrialCount, int analysisTrialCount, int failedTrialCount, CancellationToken ct = default)
    {
        if (!await EnsureAnimalDatabaseAsync(identifier, ct))
            return;

        await using var db = factory.Create(identifier);

        var session = await EnsureSessionAsync(db, sessionId, ct);
        session.EndedAt = when;
        session.CaptureTrialCount = captureTrialCount;
        session.AnalysisTrialCount = analysisTrialCount;
        session.FailedTrialCount = failedTrialCount;

        await db.SaveChangesAsync(ct);
    }

    // --- Batch analysis -----------------------------------------------------------------------------

    public async Task ApplyBatchAnalysisStartedAsync(string identifier, string sessionId, string batchId,
        DateTime when, int analysisTrialCount, CancellationToken ct = default)
    {
        if (!await EnsureAnimalDatabaseAsync(identifier, ct))
            return;

        await using var db = factory.Create(identifier);

        var session = await EnsureSessionAsync(db, sessionId, ct);
        var batch = await EnsureBatchAsync(db, session, batchId, ct);

        batch.StartedAt = when;
        batch.AnalysisTrialCount = analysisTrialCount;

        await db.SaveChangesAsync(ct);
    }

    public async Task ApplyBatchAnalysisEndedAsync(string identifier, string sessionId, string batchId,
        DateTime when, int analysisTrialCount, int failedTrialCount, CancellationToken ct = default)
    {
        if (!await EnsureAnimalDatabaseAsync(identifier, ct))
            return;

        await using var db = factory.Create(identifier);

        var session = await EnsureSessionAsync(db, sessionId, ct);
        var batch = await EnsureBatchAsync(db, session, batchId, ct);

        batch.EndedAt = when;
        batch.AnalysisTrialCount = analysisTrialCount;   // Ended repeats it and is authoritative
        batch.FailedTrialCount = failedTrialCount;

        await db.SaveChangesAsync(ct);
    }

    // --- Trial --------------------------------------------------------------------------------------

    public async Task ApplyTrialEventAsync(string identifier, ApiEventKind kind, TrialEventValues values,
        CancellationToken ct = default)
    {
        if (!await EnsureAnimalDatabaseAsync(identifier, ct))
            return;

        await using var db = factory.Create(identifier);

        var trial = await EnsureTrialForEventAsync(db, values.SessionId, values.TrialId, values.BatchId, ct);

        switch (kind)
        {
            case ApiEventKind.TrialStarted:
                trial.StartedAt = values.When;
                trial.Reason = values.Reason;
                break;

            case ApiEventKind.TrialPelletPresented:
                trial.PelletPresentedAt = values.When;
                break;

            case ApiEventKind.TrialCaptureEnded:
                trial.CaptureEndedAt = values.When;
                break;

            case ApiEventKind.TrialEnded:
                trial.EndedAt = values.When;
                trial.Result = values.Result;
                break;

            // First-seen wins: these may fire more than once, and the first sighting is the one that counts.
            case ApiEventKind.TrialPelletSeen:
                trial.PelletSeenAt ??= values.When;
                break;

            case ApiEventKind.TrialAnimalSeen:
                trial.AnimalSeenAt ??= values.When;
                break;

            case ApiEventKind.TrialRightHandSeen:
                trial.RightHandSeenAt ??= values.When;
                break;

            // Record only what we see: an *Error sets ONLY its own column. The API docs say an error implies
            // its non-error counterpart, but that inference belongs to consumers, not to this capture layer.
            case ApiEventKind.IntertrialSegmentationBegin:
                trial.IntertrialSegmentationBeginAt = values.When;
                break;

            case ApiEventKind.IntertrialSegmentationEnd:
                trial.IntertrialSegmentationEndAt = values.When;
                break;

            case ApiEventKind.IntertrialSegmentationError:
                trial.IntertrialSegmentationError = values.Error;
                break;

            case ApiEventKind.IntertrialSegmentationSave:
                trial.IntertrialSegmentationSaveAt = values.When;
                trial.IntertrialSegmentationSaveLocation = values.Location;
                break;

            case ApiEventKind.IntertrialSegmentationSaveError:
                trial.IntertrialSegmentationSaveError = values.Error;
                break;

            case ApiEventKind.IntertrialDetectionBegin:
                trial.IntertrialDetectionBeginAt = values.When;
                break;

            case ApiEventKind.IntertrialDetectionEnd:
                trial.IntertrialDetectionEndAt = values.When;
                break;

            case ApiEventKind.IntertrialDetectionError:
                trial.IntertrialDetectionError = values.Error;
                break;

            case ApiEventKind.IntertrialDetectionSave:
                trial.IntertrialDetectionSaveAt = values.When;
                trial.IntertrialDetectionSaveLocation = values.Location;
                break;

            case ApiEventKind.IntertrialDetectionSaveError:
                trial.IntertrialDetectionSaveError = values.Error;
                break;

            // Last-seen wins. The docs allow multiple per trial, but in practice this is very nearly
            // impossible, so a single overwriting column is enough.
            case ApiEventKind.IntertrialPelletShift:
                trial.IntertrialPelletShift = values.PelletShiftJson;
                break;

            default:
                LogUnhandledTrialEvent(kind);
                break;
        }

        await db.SaveChangesAsync(ct);
    }

    // --- Reach events -------------------------------------------------------------------------------

    public async Task ReplaceTrialReachEventsAsync(string identifier, string sessionId, int trialId,
        string? batchId, IReadOnlyCollection<ReachEvent> reachEvents, CancellationToken ct = default)
    {
        if (!await EnsureAnimalDatabaseAsync(identifier, ct))
            return;

        await using var db = factory.Create(identifier);

        var trial = await EnsureTrialForEventAsync(db, sessionId, trialId, batchId, ct);

        // trialReachEvents carries the trial's whole list, so a re-delivery replaces rather than appends.
        // A trial we just created has no rows to replace (and no Id to query by yet).
        if (trial.Id != 0)
        {
            // The query filter already excludes previously soft-deleted rows, so this does not re-delete them.
            var existing = await db.ReachEvents.Where(r => r.TrialId == trial.Id).ToListAsync(ct);

            if (existing.Count > 0)
                db.ReachEvents.RemoveRange(existing);   // AppDbContext turns Delete into a soft delete
        }

        foreach (var reachEvent in reachEvents)
        {
            db.ReachEvents.Add(new Entities.ReachEvent
            {
                Trial = trial,   // navigation, so this works for a trial that is not saved yet
                Method = ReachEventMethod.ToCode(reachEvent.Method),
                Outcome = ReachEventOutcome.ToCode(reachEvent.Outcome),
                FirstFrame = reachEvent.Init,
                LastFrame = reachEvent.End,
                MaxFrame = reachEvent.Max,
                DelaySincePresented = reachEvent.DelaySincePresented
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Entities.ReachEvent>> GetReachEventHistoryAsync(string identifier,
        DateTime since, CancellationToken ct = default)
    {
        if (!SqliteStorage.IsValidIdentifier(identifier))
        {
            LogRejectedIdentifier(identifier);
            return [];
        }

        // A read must not create the database; if the animal has no database yet it simply has no history.
        if (!_initialized.ContainsKey(identifier) && !File.Exists(storage.GetAnimalDatabasePath(identifier)))
            return [];

        await using var db = factory.Create(identifier);

        // No Include of Trial: the navigation would drag in Trial.ReachEvents and cycle during serialization.
        return await db.ReachEvents
            .AsNoTracking()
            .Where(r => r.CreatedAt >= since)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    // --- Reach status -------------------------------------------------------------------------------

    public async Task AddReachStatusIfChangedAsync(string identifier, ApiReachStatus total,
        ApiReachStatus day, DateOnly? dayValue, CancellationToken ct = default)
    {
        if (!await EnsureAnimalDatabaseAsync(identifier, ct))
            return;

        await using var db = factory.Create(identifier);

        var cache = _reachStatus.TryGetValue(identifier, out var cached)
            ? cached
            : await LoadReachStatusCacheAsync(db, ct);

        var totalChanged = cache.Total is not { } t || !SameCounts(t, total);
        var dayChanged = cache.Day is not { } d || !SameCounts(d, day) || cache.DayValue != dayValue;

        if (!totalChanged && !dayChanged)
        {
            _reachStatus[identifier] = cache;
            return;
        }

        if (totalChanged)
        {
            db.ReachStatusTotals.Add(new Entities.ReachStatusTotal
            {
                PelletsPresented = total.PelletsPresented,
                PelletsConsumed = total.PelletsConsumed,
                Reaches = total.Reaches,
                SuccessfulReaches = total.SuccessfulReaches
            });
        }

        if (dayChanged)
        {
            db.ReachStatusDays.Add(new Entities.ReachStatusDay
            {
                PelletsPresented = day.PelletsPresented,
                PelletsConsumed = day.PelletsConsumed,
                Reaches = day.Reaches,
                SuccessfulReaches = day.SuccessfulReaches,
                Day = dayValue
            });
        }

        await db.SaveChangesAsync(ct);

        _reachStatus[identifier] = new ReachStatusCache(
            totalChanged ? total : cache.Total,
            dayChanged ? day : cache.Day,
            dayChanged ? dayValue : cache.DayValue);
    }

    private static async Task<ReachStatusCache> LoadReachStatusCacheAsync(AnimalDbContext db, CancellationToken ct)
    {
        var total = await db.ReachStatusTotals
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var day = await db.ReachStatusDays
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

        return new ReachStatusCache(
            total is null ? null : new ApiReachStatus
            {
                PelletsPresented = total.PelletsPresented,
                PelletsConsumed = total.PelletsConsumed,
                Reaches = total.Reaches,
                SuccessfulReaches = total.SuccessfulReaches
            },
            day is null ? null : new ApiReachStatus
            {
                PelletsPresented = day.PelletsPresented,
                PelletsConsumed = day.PelletsConsumed,
                Reaches = day.Reaches,
                SuccessfulReaches = day.SuccessfulReaches
            },
            day?.Day);
    }

    private static bool SameCounts(ApiReachStatus a, ApiReachStatus b) =>
        a.PelletsPresented == b.PelletsPresented
        && a.PelletsConsumed == b.PelletsConsumed
        && a.Reaches == b.Reaches
        && a.SuccessfulReaches == b.SuccessfulReaches;

    private sealed record ReachStatusCache(ApiReachStatus? Total, ApiReachStatus? Day, DateOnly? DayValue);

    // --- Ensure, don't assume -----------------------------------------------------------------------
    //
    // The first event we see may be midstream, and batch ids do not exist until analysis starts, so no write
    // may assume its related rows exist. Each ensure attaches the row (with nulls for whatever we missed) if
    // it is not already there.
    //
    // Nothing here saves. Rows are wired to their parents by NAVIGATION PROPERTY rather than by foreign-key
    // id, so EF orders the inserts and fills the keys in itself -- which lets an event that touches a
    // session, a trial and a batch persist all of it in ONE SaveChanges instead of one per row.
    //
    // A row that has just been added (and not yet saved) still has Id == 0, which is what IsUnsaved reads.
    // It also means there is nothing in the database to look up beneath it, so the lookups below are skipped
    // for a brand-new parent rather than issuing a query that cannot match.

    private static bool IsUnsaved(Entities.Session session) => session.Id == 0;

    private static async Task<Entities.Session> EnsureSessionAsync(AnimalDbContext db, string sessionId,
        CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Identifier == sessionId, ct);

        if (session is null)
        {
            session = new Entities.Session { Identifier = sessionId };
            db.Sessions.Add(session);
        }

        return session;
    }

    private static async Task<Entities.BatchAnalysis> EnsureBatchAsync(AnimalDbContext db,
        Entities.Session session, string batchId, CancellationToken ct)
    {
        // A batch only exists once its session does, so a brand-new session cannot have one yet.
        var batch = IsUnsaved(session)
            ? null
            : await db.BatchAnalyses.FirstOrDefaultAsync(b => b.Identifier == batchId, ct);

        if (batch is null)
        {
            batch = new Entities.BatchAnalysis { Identifier = batchId, Session = session };
            db.BatchAnalyses.Add(batch);
        }

        return batch;
    }

    private static async Task<Entities.Trial> EnsureTrialForEventAsync(AnimalDbContext db, string sessionId,
        int trialId, string? batchId, CancellationToken ct)
    {
        var session = await EnsureSessionAsync(db, sessionId, ct);

        var trial = IsUnsaved(session)
            ? null
            : await db.Trials.FirstOrDefaultAsync(
                t => t.SessionId == session.Id && t.Identifier == trialId, ct);

        if (trial is null)
        {
            trial = new Entities.Trial { Identifier = trialId, Session = session };
            db.Trials.Add(trial);
        }

        // This event may be the FIRST sight of the batch: attach it and point the trial at it.
        if (!string.IsNullOrWhiteSpace(batchId))
            trial.BatchAnalysis = await EnsureBatchAsync(db, session, batchId, ct);

        return trial;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected invalid animal identifier '{identifier}'.")]
    private partial void LogRejectedIdentifier(string identifier);

    [LoggerMessage(Level = LogLevel.Information, Message = "Animal database ready for '{identifier}'.")]
    private partial void LogAnimalDatabaseReady(string identifier);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No Trial column mapped for event {kind}; nothing recorded.")]
    private partial void LogUnhandledTrialEvent(ApiEventKind kind);
}
