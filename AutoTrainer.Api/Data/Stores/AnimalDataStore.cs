using System.Collections.Concurrent;
using System.Linq.Expressions;
using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Endpoints;
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

// Trials-by-session query filters. Empty ReachMethods/ReachOutcomes means "no reach filter". AnalysisPerformed
// is defined as: Result present and not capture_only (see GetTrialsAsync).
public readonly record struct TrialFilter(bool? AnalysisPerformed, int[] ReachMethods, int[] ReachOutcomes);

// Sort fields the trials-by-session queries understand (both the /trials list and the session expand=trials
// path). Only `identifier` is supported today; the shared `sort` convention (see SortRequest) and default
// order are otherwise the same as every other list endpoint.
public static class TrialSort
{
    public const string Identifier = "identifier";

    // A blank/absent sort keeps the default order, so it's always supported; otherwise the field must be known.
    public static bool IsSupported(SortRequest sort) => !sort.HasSort || sort.Is(Identifier);

    // Applies the trial order: the requested field, else the default (newest started first). Identifier is unique
    // within a session, so the Id tiebreaker just keeps paging deterministic.
    public static IOrderedQueryable<Entities.Trial> Apply(IQueryable<Entities.Trial> q, SortRequest sort) =>
        sort.Is(Identifier)
            ? (sort.Descending ? q.OrderByDescending(t => t.Identifier) : q.OrderBy(t => t.Identifier)).ThenBy(t => t.Id)
            : q.OrderByDescending(t => t.StartedAt).ThenByDescending(t => t.Id);
}

// Which of the two reach-status tables a single-column count-change event targets, and which column. The
// CountChanged events (ReachStatusTotal) and their Day counterparts (ReachStatusDay) each carry the new
// absolute value for exactly one of these columns.
public enum ReachCountScope { Total, Day }

public enum ReachCountField { PelletsPresented, PelletsConsumed, Reaches, SuccessfulReaches }

// The running 5-day reach-status total (Animal.ReachStatus5Day): the sum of the latest row per calendar day
// over the fixed window [CurrentDay-4, CurrentDay] of the day table. It is split so the frequently-changing
// current day can be updated in memory without re-summing the earlier four days — Total = PriorFourSum +
// CurrentDayCounts. CurrentDay is the DayPath day the window is anchored on, null when the day table is empty.
public readonly record struct FiveDayReachStatus(
    DateOnly? CurrentDay, ApiReachStatus PriorFourSum, ApiReachStatus CurrentDayCounts)
{
    public ApiReachStatus Total => Add(PriorFourSum, CurrentDayCounts);

    public static ApiReachStatus Add(ApiReachStatus a, ApiReachStatus b) => new()
    {
        PelletsPresented = a.PelletsPresented + b.PelletsPresented,
        PelletsConsumed = a.PelletsConsumed + b.PelletsConsumed,
        Reaches = a.Reaches + b.Reaches,
        SuccessfulReaches = a.SuccessfulReaches + b.SuccessfulReaches
    };
}

public interface IAnimalDataStore
{
    Task<bool> EnsureAnimalDatabaseAsync(string identifier, CancellationToken ct = default);

    Task AddAnimalHistoryAsync(ApiAnimalStatus status, CancellationToken ct = default);

    Task ApplySessionStartedAsync(string identifier, string sessionId, DateTime when,
        bool isAnalysisDeferred, CancellationToken ct = default);

    // Applies the session-ended columns and returns the updated session's scalar summary. Null only when the
    // database can't be ensured.
    Task<SessionSummaryDto?> ApplySessionEndedAsync(string identifier, string sessionId, DateTime when,
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

    // A single-column update to one of the reach-status tables (a *CountChanged event). `count` is the new
    // absolute value for that column; the row's other counts are carried forward from the most recent row. For
    // Day scope, `day` is the attribution day (the newer of the latest DayPath and DayStarted days) — a change
    // in it is a rollover, so the new day's other columns start at zero instead of carrying forward. Ignored for
    // Total scope.
    Task ApplyReachCountChangeAsync(string identifier, ReachCountScope scope, ReachCountField field,
        int count, DateOnly? day, CancellationToken ct = default);

    // --- Read side (REST query API) -----------------------------------------------------------------
    // All reads follow read-without-create: an animal with no database yet simply has no data (empty/absent),
    // and the read never migrates or creates the database.

    Task<PagedResult<ReachEventDto>> GetReachEventsAsync(string identifier, DateTime since, int[] methods,
        int[] outcomes, PageRequest page, CancellationToken ct = default);

    Task<int> CountReachEventsAsync(string identifier, DateTime since, int[] methods, int[] outcomes,
        CancellationToken ct = default);

    // since is optional: null means no time window (return every session for the animal).
    Task<PagedResult<SessionSummaryDto>> GetSessionsAsync(string identifier, DateTime? since,
        bool? isAnalysisDeferred, PageRequest page, CancellationToken ct = default);

    Task<int> CountSessionsAsync(string identifier, DateTime? since, bool? isAnalysisDeferred,
        CancellationToken ct = default);

    Task<SessionDetailDto?> GetSessionAsync(string identifier, string sessionId, SessionExpand expand,
        SortRequest trialSort, CancellationToken ct = default);

    Task<PagedResult<TrialDto>> GetTrialsAsync(string identifier, string sessionId, TrialFilter filter,
        SortRequest sort, PageRequest page, CancellationToken ct = default);

    Task<int> CountTrialsAsync(string identifier, string sessionId, TrialFilter filter,
        CancellationToken ct = default);

    Task<TrialDto?> GetTrialAsync(string identifier, string sessionId, int trialId, bool expandReaches,
        CancellationToken ct = default);

    Task<PagedResult<BatchAnalysisDto>> GetBatchesAsync(string identifier, string sessionId, PageRequest page,
        CancellationToken ct = default);

    Task<AnimalDetailDto?> GetAnimalDetailAsync(string identifier, CancellationToken ct = default);

    // The most recent Total row and the most recent Day row for `deviceDay` (null day -> no day portion). Reach
    // columns only. Portions are null when the row is absent; never throws for a missing database.
    Task<AnimalReachStatusDto> GetReachStatusAsync(string identifier, DateOnly? deviceDay,
        CancellationToken ct = default);

    // Seeds the running 5-day reach total from the day table: the latest row per calendar day over the fixed
    // window [anchor-4, anchor], where anchor is `anchorDay` or, when null, the most recent stored day. Days
    // with no row contribute nothing. Read only — used on animal selection (anchor null) and a day rollover.
    Task<FiveDayReachStatus> LoadFiveDayReachStatusAsync(string identifier, DateOnly? anchorDay,
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

    public async Task<SessionSummaryDto?> ApplySessionEndedAsync(string identifier, string sessionId, DateTime when,
        int captureTrialCount, int analysisTrialCount, int failedTrialCount, CancellationToken ct = default)
    {
        if (!await EnsureAnimalDatabaseAsync(identifier, ct))
            return null;

        await using var db = factory.Create(identifier);

        var session = await EnsureSessionAsync(db, sessionId, ct);
        session.EndedAt = when;
        session.CaptureTrialCount = captureTrialCount;
        session.AnalysisTrialCount = analysisTrialCount;
        session.FailedTrialCount = failedTrialCount;

        await db.SaveChangesAsync(ct);

        return new SessionSummaryDto(session.Identifier, session.StartedAt, session.EndedAt,
            session.IsAnalysisDeferred, session.CaptureTrialCount, session.AnalysisTrialCount,
            session.FailedTrialCount);
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

    // A read must never create the database. True only when the identifier is valid AND the animal's database
    // already exists (in-process memo or on disk) — otherwise the animal simply has no data yet.
    private bool CanRead(string identifier)
    {
        if (!SqliteStorage.IsValidIdentifier(identifier))
        {
            LogRejectedIdentifier(identifier);
            return false;
        }

        return _initialized.ContainsKey(identifier) || File.Exists(storage.GetAnimalDatabasePath(identifier));
    }

    // Trial projection shared by the trials list and the single-trial/session-detail reads. Two variants so the
    // reach children are populated only on the drill-down; the scalar columns are otherwise identical.
    private static readonly Expression<Func<Entities.Trial, TrialDto>> ProjectTrialNoReaches = t => new TrialDto(
        t.Identifier, t.BatchAnalysis != null ? t.BatchAnalysis.Identifier : null, t.Reason, t.Result,
        t.StartedAt, t.PelletPresentedAt, t.PelletSeenAt, t.AnimalSeenAt, t.RightHandSeenAt, t.CaptureEndedAt,
        t.EndedAt, t.IntertrialSegmentationBeginAt, t.IntertrialSegmentationEndAt, t.IntertrialSegmentationError,
        t.IntertrialSegmentationSaveAt, t.IntertrialSegmentationSaveLocation, t.IntertrialSegmentationSaveError,
        t.IntertrialDetectionBeginAt, t.IntertrialDetectionEndAt, t.IntertrialDetectionError,
        t.IntertrialDetectionSaveAt, t.IntertrialDetectionSaveLocation, t.IntertrialDetectionSaveError,
        t.IntertrialPelletShift, t.ReachEvents.Count, null);

    private static readonly Expression<Func<Entities.Trial, TrialDto>> ProjectTrialWithReaches = t => new TrialDto(
        t.Identifier, t.BatchAnalysis != null ? t.BatchAnalysis.Identifier : null, t.Reason, t.Result,
        t.StartedAt, t.PelletPresentedAt, t.PelletSeenAt, t.AnimalSeenAt, t.RightHandSeenAt, t.CaptureEndedAt,
        t.EndedAt, t.IntertrialSegmentationBeginAt, t.IntertrialSegmentationEndAt, t.IntertrialSegmentationError,
        t.IntertrialSegmentationSaveAt, t.IntertrialSegmentationSaveLocation, t.IntertrialSegmentationSaveError,
        t.IntertrialDetectionBeginAt, t.IntertrialDetectionEndAt, t.IntertrialDetectionError,
        t.IntertrialDetectionSaveAt, t.IntertrialDetectionSaveLocation, t.IntertrialDetectionSaveError,
        t.IntertrialPelletShift, t.ReachEvents.Count,
        t.ReachEvents.OrderBy(r => r.Id).Select(r => new ReachEventDto(r.Id, r.CreatedAt, r.TrialId, r.Method,
            r.Outcome, r.FirstFrame, r.LastFrame, r.MaxFrame, r.DelaySincePresented)).ToList());

    public async Task<PagedResult<ReachEventDto>> GetReachEventsAsync(string identifier, DateTime since,
        int[] methods, int[] outcomes, PageRequest page, CancellationToken ct = default)
    {
        if (!CanRead(identifier))
            return PagedResult<ReachEventDto>.Empty(page.Page, page.PageSize);

        await using var db = factory.Create(identifier);

        var q = FilteredReachEvents(db, since, methods, outcomes);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(r => new ReachEventDto(r.Id, r.CreatedAt, r.TrialId, r.Method, r.Outcome,
                r.FirstFrame, r.LastFrame, r.MaxFrame, r.DelaySincePresented))
            .ToListAsync(ct);

        return new PagedResult<ReachEventDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<int> CountReachEventsAsync(string identifier, DateTime since, int[] methods, int[] outcomes,
        CancellationToken ct = default)
    {
        if (!CanRead(identifier))
            return 0;

        await using var db = factory.Create(identifier);
        return await FilteredReachEvents(db, since, methods, outcomes).CountAsync(ct);
    }

    // Shared filtered query behind GetReachEventsAsync/CountReachEventsAsync (the count skips ordering/paging).
    private static IQueryable<Entities.ReachEvent> FilteredReachEvents(AnimalDbContext db, DateTime since,
        int[] methods, int[] outcomes)
    {
        var q = db.ReachEvents.AsNoTracking().Where(r => r.CreatedAt >= since);
        if (methods.Length > 0) q = q.Where(r => methods.Contains(r.Method));
        if (outcomes.Length > 0) q = q.Where(r => outcomes.Contains(r.Outcome));
        return q;
    }

    public async Task<PagedResult<SessionSummaryDto>> GetSessionsAsync(string identifier, DateTime? since,
        bool? isAnalysisDeferred, PageRequest page, CancellationToken ct = default)
    {
        if (!CanRead(identifier))
            return PagedResult<SessionSummaryDto>.Empty(page.Page, page.PageSize);

        await using var db = factory.Create(identifier);

        var q = FilteredSessions(db, since, isAnalysisDeferred);

        var total = await q.CountAsync(ct);

        // Sort by StartedAt desc (SQLite orders NULLs last under DESC — no-start sessions fall to the bottom).
        var items = await q
            .OrderByDescending(s => s.StartedAt).ThenByDescending(s => s.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(s => new SessionSummaryDto(s.Identifier, s.StartedAt, s.EndedAt, s.IsAnalysisDeferred,
                s.CaptureTrialCount, s.AnalysisTrialCount, s.FailedTrialCount))
            .ToListAsync(ct);

        return new PagedResult<SessionSummaryDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<int> CountSessionsAsync(string identifier, DateTime? since, bool? isAnalysisDeferred,
        CancellationToken ct = default)
    {
        if (!CanRead(identifier))
            return 0;

        await using var db = factory.Create(identifier);
        return await FilteredSessions(db, since, isAnalysisDeferred).CountAsync(ct);
    }

    // Shared filtered query behind GetSessionsAsync/CountSessionsAsync. Time window is on CreatedAt (always
    // present) so a session first seen mid-stream (null StartedAt) is not dropped; no window returns all.
    private static IQueryable<Entities.Session> FilteredSessions(AnimalDbContext db, DateTime? since,
        bool? isAnalysisDeferred)
    {
        var q = db.Sessions.AsNoTracking();
        if (since is { } s)
            q = q.Where(x => x.CreatedAt >= s);
        if (isAnalysisDeferred is { } d)
            q = q.Where(x => x.IsAnalysisDeferred == d);
        return q;
    }

    public async Task<SessionDetailDto?> GetSessionAsync(string identifier, string sessionId,
        SessionExpand expand, SortRequest trialSort, CancellationToken ct = default)
    {
        if (!CanRead(identifier))
            return null;

        await using var db = factory.Create(identifier);

        var s = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(x => x.Identifier == sessionId, ct);
        if (s is null)
            return null;

        IReadOnlyList<TrialDto>? trials = null;
        if (expand.Trials)
        {
            var tq = TrialSort.Apply(db.Trials.AsNoTracking().Where(t => t.SessionId == s.Id), trialSort);
            trials = expand.TrialsReaches
                ? await tq.Select(ProjectTrialWithReaches).ToListAsync(ct)
                : await tq.Select(ProjectTrialNoReaches).ToListAsync(ct);
        }

        IReadOnlyList<BatchAnalysisDto>? batches = null;
        if (expand.Batches)
        {
            batches = await db.BatchAnalyses.AsNoTracking().Where(b => b.SessionId == s.Id)
                .OrderByDescending(b => b.StartedAt).ThenByDescending(b => b.Id)
                .Select(b => new BatchAnalysisDto(b.Identifier, b.StartedAt, b.EndedAt,
                    b.AnalysisTrialCount, b.FailedTrialCount))
                .ToListAsync(ct);
        }

        return new SessionDetailDto(s.Identifier, s.StartedAt, s.EndedAt, s.IsAnalysisDeferred,
            s.CaptureTrialCount, s.AnalysisTrialCount, s.FailedTrialCount, trials, batches);
    }

    public async Task<PagedResult<TrialDto>> GetTrialsAsync(string identifier, string sessionId,
        TrialFilter filter, SortRequest sort, PageRequest page, CancellationToken ct = default)
    {
        if (!CanRead(identifier))
            return PagedResult<TrialDto>.Empty(page.Page, page.PageSize);

        await using var db = factory.Create(identifier);

        var sid = await db.Sessions.Where(x => x.Identifier == sessionId)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (sid is null)
            return PagedResult<TrialDto>.Empty(page.Page, page.PageSize);

        var q = FilteredTrials(db, sid.Value, filter);

        var total = await q.CountAsync(ct);

        var items = await TrialSort.Apply(q, sort)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(ProjectTrialNoReaches)
            .ToListAsync(ct);

        return new PagedResult<TrialDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<int> CountTrialsAsync(string identifier, string sessionId, TrialFilter filter,
        CancellationToken ct = default)
    {
        if (!CanRead(identifier))
            return 0;

        await using var db = factory.Create(identifier);

        var sid = await db.Sessions.Where(x => x.Identifier == sessionId)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (sid is null)
            return 0;

        return await FilteredTrials(db, sid.Value, filter).CountAsync(ct);
    }

    // Shared filtered query behind GetTrialsAsync/CountTrialsAsync (the count skips sorting/paging/projection).
    private static IQueryable<Entities.Trial> FilteredTrials(AnimalDbContext db, int sid, TrialFilter filter)
    {
        var q = db.Trials.AsNoTracking().Where(t => t.SessionId == sid);

        // analysisPerformed := Result present and not capture_only.
        if (filter.AnalysisPerformed is { } ap)
            q = ap
                ? q.Where(t => t.Result != null && t.Result != CaptureAnalysisResult.CaptureOnly)
                : q.Where(t => t.Result == null || t.Result == CaptureAnalysisResult.CaptureOnly);

        // Deep (graph) filter: trials that HAVE a matching reach child. EXISTS over the navigation; the
        // soft-delete query filter is applied to ReachEvents inside .Any() too.
        var m = filter.ReachMethods;
        var o = filter.ReachOutcomes;
        if (m.Length > 0 && o.Length > 0)
            q = q.Where(t => t.ReachEvents.Any(r => m.Contains(r.Method) && o.Contains(r.Outcome)));
        else if (m.Length > 0)
            q = q.Where(t => t.ReachEvents.Any(r => m.Contains(r.Method)));
        else if (o.Length > 0)
            q = q.Where(t => t.ReachEvents.Any(r => o.Contains(r.Outcome)));

        return q;
    }

    public async Task<TrialDto?> GetTrialAsync(string identifier, string sessionId, int trialId,
        bool expandReaches, CancellationToken ct = default)
    {
        if (!CanRead(identifier))
            return null;

        await using var db = factory.Create(identifier);

        var sid = await db.Sessions.Where(x => x.Identifier == sessionId)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (sid is null)
            return null;

        var q = db.Trials.AsNoTracking().Where(t => t.SessionId == sid.Value && t.Identifier == trialId);

        return expandReaches
            ? await q.Select(ProjectTrialWithReaches).FirstOrDefaultAsync(ct)
            : await q.Select(ProjectTrialNoReaches).FirstOrDefaultAsync(ct);
    }

    public async Task<PagedResult<BatchAnalysisDto>> GetBatchesAsync(string identifier, string sessionId,
        PageRequest page, CancellationToken ct = default)
    {
        if (!CanRead(identifier))
            return PagedResult<BatchAnalysisDto>.Empty(page.Page, page.PageSize);

        await using var db = factory.Create(identifier);

        var sid = await db.Sessions.Where(x => x.Identifier == sessionId)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (sid is null)
            return PagedResult<BatchAnalysisDto>.Empty(page.Page, page.PageSize);

        var q = db.BatchAnalyses.AsNoTracking().Where(b => b.SessionId == sid.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(b => b.StartedAt).ThenByDescending(b => b.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(b => new BatchAnalysisDto(b.Identifier, b.StartedAt, b.EndedAt,
                b.AnalysisTrialCount, b.FailedTrialCount))
            .ToListAsync(ct);

        return new PagedResult<BatchAnalysisDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<AnimalDetailDto?> GetAnimalDetailAsync(string identifier, CancellationToken ct = default)
    {
        if (!CanRead(identifier))
            return null;

        await using var db = factory.Create(identifier);

        var history = await db.AnimalHistory.AsNoTracking()
            .OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id).FirstOrDefaultAsync(ct);
        var total = await db.ReachStatusTotals.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        var day = await db.ReachStatusDays.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);

        // DB exists but carries nothing about this animal yet -> treat as absent for the detail endpoint.
        if (history is null && total is null && day is null)
            return null;

        // Rolling 5-day total from the day table (aggregate -> null Day, like the total snapshot). Null when
        // there are no day rows to sum.
        var five = await ComputeFiveDayReachStatusAsync(db, null, ct);
        var fiveSnapshot = five.CurrentDay is null
            ? null
            : new ReachStatusSnapshotDto(five.Total.PelletsPresented, five.Total.PelletsConsumed,
                five.Total.Reaches, five.Total.SuccessfulReaches, null);

        return new AnimalDetailDto(
            identifier,
            history?.Name ?? "",
            history?.DcsSendX ?? 0,
            history?.DcsSendY ?? 0,
            history?.DcsSendZ ?? 0,
            history?.TargetYLimit,
            total is null
                ? null
                : new ReachStatusSnapshotDto(total.PelletsPresented, total.PelletsConsumed,
                    total.Reaches, total.SuccessfulReaches, null),
            day is null
                ? null
                : new ReachStatusSnapshotDto(day.PelletsPresented, day.PelletsConsumed,
                    day.Reaches, day.SuccessfulReaches, day.Day),
            fiveSnapshot);
    }

    public async Task<FiveDayReachStatus> LoadFiveDayReachStatusAsync(string identifier, DateOnly? anchorDay,
        CancellationToken ct = default)
    {
        if (!CanRead(identifier))
            return default;

        await using var db = factory.Create(identifier);
        return await ComputeFiveDayReachStatusAsync(db, anchorDay, ct);
    }

    // The 5-day window computation over an already-open context, shared by LoadFiveDayReachStatusAsync (its own
    // connection) and GetAnimalDetailAsync (reuses its connection).
    private static async Task<FiveDayReachStatus> ComputeFiveDayReachStatusAsync(AnimalDbContext db,
        DateOnly? anchorDay, CancellationToken ct)
    {
        // Anchor at the caller's day (a rollover), else the most recent stored day (selection). When null is
        // passed and the table is empty the query yields null and there is nothing to sum.
        var anchor = anchorDay ?? await db.ReachStatusDays.AsNoTracking()
            .Where(r => r.Day != null)
            .OrderByDescending(r => r.Day)
            .Select(r => r.Day)
            .FirstOrDefaultAsync(ct);

        if (anchor is not { } a)
            return default;

        var lo = a.AddDays(-4);

        // The window can hold many rows per day; keep only the latest (max Id) per day, then load those rows.
        var latestIds = await db.ReachStatusDays.AsNoTracking()
            .Where(r => r.Day != null && r.Day >= lo && r.Day <= a)
            .GroupBy(r => r.Day!.Value)
            .Select(g => g.Max(r => r.Id))
            .ToListAsync(ct);

        var rows = await db.ReachStatusDays.AsNoTracking()
            .Where(r => latestIds.Contains(r.Id))
            .ToListAsync(ct);

        var current = default(ApiReachStatus);
        var prior = default(ApiReachStatus);
        foreach (var r in rows)
        {
            var counts = new ApiReachStatus
            {
                PelletsPresented = r.PelletsPresented,
                PelletsConsumed = r.PelletsConsumed,
                Reaches = r.Reaches,
                SuccessfulReaches = r.SuccessfulReaches
            };

            if (r.Day == a)
                current = counts;
            else
                prior = FiveDayReachStatus.Add(prior, counts);
        }

        return new FiveDayReachStatus(a, prior, current);
    }

    public async Task<AnimalReachStatusDto> GetReachStatusAsync(string identifier, DateOnly? deviceDay,
        CancellationToken ct = default)
    {
        if (!CanRead(identifier))
            return new AnimalReachStatusDto(null, null);

        await using var db = factory.Create(identifier);

        var total = await db.ReachStatusTotals.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);

        // Day portion only when the device day is known, and only the row for that exact day — a leftover row
        // from a previous day is not "today".
        Entities.ReachStatusDay? day = null;
        if (deviceDay is { } d)
            day = await db.ReachStatusDays.AsNoTracking()
                .Where(r => r.Day == d)
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);

        return new AnimalReachStatusDto(
            total is null
                ? null
                : new ReachStatusSnapshotDto(total.PelletsPresented, total.PelletsConsumed,
                    total.Reaches, total.SuccessfulReaches, null),
            day is null
                ? null
                : new ReachStatusSnapshotDto(day.PelletsPresented, day.PelletsConsumed,
                    day.Reaches, day.SuccessfulReaches, day.Day));
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

    public async Task ApplyReachCountChangeAsync(string identifier, ReachCountScope scope, ReachCountField field,
        int count, DateOnly? day, CancellationToken ct = default)
    {
        if (!await EnsureAnimalDatabaseAsync(identifier, ct))
            return;

        await using var db = factory.Create(identifier);

        var cache = _reachStatus.TryGetValue(identifier, out var cached)
            ? cached
            : await LoadReachStatusCacheAsync(db, ct);

        if (scope == ReachCountScope.Total)
        {
            // Totals never reset: carry the latest snapshot forward and set the one changed column.
            var current = cache.Total ?? default;
            var updated = WithCount(current, field, count);
            if (SameCounts(current, updated))
            {
                _reachStatus[identifier] = cache;
                return;
            }

            db.ReachStatusTotals.Add(new Entities.ReachStatusTotal
            {
                PelletsPresented = updated.PelletsPresented,
                PelletsConsumed = updated.PelletsConsumed,
                Reaches = updated.Reaches,
                SuccessfulReaches = updated.SuccessfulReaches
            });

            await db.SaveChangesAsync(ct);
            _reachStatus[identifier] = cache with { Total = updated };
            return;
        }

        // Day scope: the event's attribution day (caller-supplied) decides the row's Day. A day different from
        // the last one is a rollover — the producer resets its day counters, so the new day starts from zero
        // rather than carrying the previous day's columns forward. Fall back to the cached day when unknown.
        var effectiveDay = day ?? cache.DayValue;
        var isNewDay = day is { } d && d != cache.DayValue;

        var dayBase = isNewDay ? default : cache.Day ?? default;
        var dayUpdated = WithCount(dayBase, field, count);

        // A new day must be recorded even if the counts coincide (mirrors AddReachStatusIfChangedAsync); within a
        // day, an unchanged column appends nothing.
        if (!isNewDay && SameCounts(dayBase, dayUpdated))
        {
            _reachStatus[identifier] = cache;
            return;
        }

        db.ReachStatusDays.Add(new Entities.ReachStatusDay
        {
            PelletsPresented = dayUpdated.PelletsPresented,
            PelletsConsumed = dayUpdated.PelletsConsumed,
            Reaches = dayUpdated.Reaches,
            SuccessfulReaches = dayUpdated.SuccessfulReaches,
            Day = effectiveDay
        });

        await db.SaveChangesAsync(ct);
        _reachStatus[identifier] = cache with { Day = dayUpdated, DayValue = effectiveDay };
    }

    private static ApiReachStatus WithCount(ApiReachStatus s, ReachCountField field, int count) => field switch
    {
        ReachCountField.PelletsPresented => s with { PelletsPresented = count },
        ReachCountField.PelletsConsumed => s with { PelletsConsumed = count },
        ReachCountField.Reaches => s with { Reaches = count },
        ReachCountField.SuccessfulReaches => s with { SuccessfulReaches = count },
        _ => s
    };

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
