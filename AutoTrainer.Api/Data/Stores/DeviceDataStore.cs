using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Endpoints;
using AutoTrainer.Api.Models;
using Entities = AutoTrainer.Api.Data.Entities;

namespace AutoTrainer.Api.Data.Stores;

public interface IDeviceDataStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task AddSystemConfigurationAsync(ApiSystemConfiguration config, CancellationToken ct = default);
    Task AddAlarmHistoryAsync(ApiAlarmStatus status, CancellationToken ct = default);
    Task AddDetectorHistoryAsync(ApiDetectorStatus status, CancellationToken ct = default);

    // One row per emergency event. Both return the primary key of the row just inserted -- the notification
    // rendezvous stamps by that id, never by a query. notificationSentAt is non-null only when the
    // notification for this event was already published before the insert ran, in which case the row is
    // written already-stamped rather than updated afterwards.
    Task<int> AddEmergencyStopAsync(ApiEmergencyStopPayload payload, DateTime occurredAt, long eventIndex,
        DateTime? notificationSentAt = null, CancellationToken ct = default);
    Task<int> AddEmergencyResumeAsync(ApiEmergencyResumePayload payload, DateTime occurredAt, long eventIndex,
        DateTime? notificationSentAt = null, CancellationToken ct = default);

    // Stamps one specific row, identified by the primary key returned from the insert. Returns false when
    // the row does not exist or is already stamped. Never throws for a miss.
    Task<bool> MarkNotificationSentAsync(int id, DateTime sentAt, CancellationToken ct = default);

    // Paged, time-windowed, type-filtered reads for the REST query API. An empty alarmIds/detectorIds means
    // "no type filter". Newest-first by observation time.
    // isEnabled is null for "either". These are history rows, so it selects observations recorded while the
    // alarm/detector was in that state -- not what is enabled right now (see GET /device for current state).
    Task<PagedResult<AlarmDto>> GetAlarmsAsync(DateTime since, ApiAlarmKind[] alarmIds, bool? isEnabled,
        PageRequest page, CancellationToken ct = default);
    Task<PagedResult<DetectorDto>> GetDetectorsAsync(DateTime since, ApiDetectorKind[] detectorIds, bool? isEnabled,
        PageRequest page, CancellationToken ct = default);

    // Windowed on OccurredAt (the producer's event time, and the indexed column) rather than on the row's
    // write time. An empty kinds array means "both stop and resume". Newest-first.
    //
    // The reason filters are per-direction because the two enums are separate types that share integer
    // values, so a code only identifies a reason once the kind is known. Each empty array means "any reason
    // of that kind"; the caller is responsible for not combining a stop-reason filter with a resume-kind
    // filter, which would match nothing.
    Task<PagedResult<EmergencyDto>> GetEmergenciesAsync(DateTime since, ApiEventKind[] kinds,
        ApiEmergencyStopReason[] stopReasons, ApiEmergencyResumeReason[] resumeReasons, PageRequest page,
        CancellationToken ct = default);

    // The flat cross-animal registry list: a single indexed query on the device database, no per-animal files.
    Task<IReadOnlyList<AnimalDto>> GetAnimalsAsync(CancellationToken ct = default);

    // Ensure the device-level registry has a row for this animal (created when its per-animal database is
    // first created). No-op if it is already registered; does not touch the name.
    Task RegisterAnimalAsync(string identifier, CancellationToken ct = default);

    // Set the registry name for an animal (from .animalSelected/.animalUpdated). Creates the row if missing,
    // and only writes when the name actually changes.
    Task SetAnimalNameAsync(string identifier, string name, CancellationToken ct = default);
}

public partial class DeviceDataStore(
    IDbContextFactory<DeviceDbContext> factory,
    ILogger<DeviceDataStore> logger) : IDeviceDataStore
{
    private readonly ILogger<DeviceDataStore> _logger = logger;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        LogMigratingDatabase();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
        LogDatabaseReady();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrating device database...")]
    private partial void LogMigratingDatabase();

    [LoggerMessage(Level = LogLevel.Information, Message = "Device database ready.")]
    private partial void LogDatabaseReady();

    // The raw string is still persisted; this only flags that the producer sent something the enum does not
    // cover yet, so a new reason gets noticed instead of being silently flattened to Unknown.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Unrecognized {kind} reason '{reason}'; stored as Unknown.")]
    private partial void LogUnknownEmergencyReason(ApiEventKind kind, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No unstamped emergency history row {id}; notification stamp skipped.")]
    private partial void LogNotificationStampSkipped(int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read stored ActiveAlarms '{json}'; reported as empty.")]
    private partial void LogUnreadableActiveAlarms(string json);

    public async Task AddSystemConfigurationAsync(ApiSystemConfiguration config, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        db.SystemConfigurations.Add(new Entities.SystemConfiguration
        {
            ApplicationVersion = config.ApplicationVersion,
            DeviceId = config.DeviceId,
            ConfigurationLocation = config.ConfigurationLocation,
            DataLocation = config.DataLocation,
            AnimalLocation = config.AnimalLocation,
            LogLocation = config.LogLocation,
            InferenceModel = config.InferenceModel
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task AddAlarmHistoryAsync(ApiAlarmStatus status, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        db.AlarmHistory.Add(new Entities.AlarmHistory
        {
            AlarmId = status.AlarmId != 0 ? status.AlarmId : status.DetectorId,
            DetectorId = status.DetectorId,
            IsActive = status.IsActive,
            IsEnabled = status.IsEnabled,
            IsAutoResumeEnabled = status.IsAutoResumeEnabled,
            IsStopCondition = status.IsStopCondition
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task AddDetectorHistoryAsync(ApiDetectorStatus status, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        db.DetectorHistory.Add(new Entities.DetectorHistory
        {
            DetectorId = status.DetectorId,
            IsActive = status.IsActive,
            IsEnabled = status.IsEnabled
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<int> AddEmergencyStopAsync(ApiEmergencyStopPayload payload, DateTime occurredAt,
        long eventIndex, DateTime? notificationSentAt = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var reasonId = EmergencyStopReasons.From(payload.ReasonCode, payload.Reason);

        if (reasonId == ApiEmergencyStopReason.Unknown)
            LogUnknownEmergencyReason(ApiEventKind.EmergencyStop, payload.Reason);

        var entity = new Entities.EmergencyStopHistory
        {
            Kind = ApiEventKind.EmergencyStop,
            OccurredAt = occurredAt,
            EventIndex = eventIndex,
            StopReasonId = reasonId,
            ReasonText = payload.Reason,
            // The ints, not the List<ApiAlarmKind>: the stored format must not depend on whether a
            // JsonStringEnumConverter is ever added to JsonDefaults.CamelCase.
            ActiveAlarms = JsonSerializer.Serialize(payload.ActiveAlarms.Select(a => (int)a)),
            NotificationSentAt = notificationSentAt
        };

        db.EmergencyStopHistory.Add(entity);
        await db.SaveChangesAsync(ct);

        return entity.Id;
    }

    public async Task<int> AddEmergencyResumeAsync(ApiEmergencyResumePayload payload, DateTime occurredAt,
        long eventIndex, DateTime? notificationSentAt = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var reasonId = EmergencyResumeReasons.From(payload.ReasonCode, payload.Reason);

        if (reasonId == ApiEmergencyResumeReason.Unknown)
            LogUnknownEmergencyReason(ApiEventKind.EmergencyResume, payload.Reason);

        var entity = new Entities.EmergencyStopHistory
        {
            Kind = ApiEventKind.EmergencyResume,
            OccurredAt = occurredAt,
            EventIndex = eventIndex,
            ResumeReasonId = reasonId,
            ReasonText = payload.Reason,
            ActiveAlarms = null,   // the resume payload has no such field; null distinguishes it from "[]"
            NotificationSentAt = notificationSentAt
        };

        db.EmergencyStopHistory.Add(entity);
        await db.SaveChangesAsync(ct);

        return entity.Id;
    }

    public async Task<bool> MarkNotificationSentAsync(int id, DateTime sentAt, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = await db.EmergencyStopHistory.FirstOrDefaultAsync(r => r.Id == id, ct);

        // Missing or already stamped: leave the original sentAt alone, so re-submitting is a no-op.
        if (row is null || row.NotificationSentAt is not null)
        {
            LogNotificationStampSkipped(id);
            return false;
        }

        row.NotificationSentAt = sentAt;
        await db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<PagedResult<AlarmDto>> GetAlarmsAsync(DateTime since, ApiAlarmKind[] alarmIds,
        bool? isEnabled, PageRequest page, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var q = db.AlarmHistory.AsNoTracking().Where(a => a.CreatedAt >= since);
        if (alarmIds.Length > 0)
            q = q.Where(a => alarmIds.Contains(a.AlarmId));   // filter on the canonical AlarmId

        if (isEnabled is { } enabled)
            q = q.Where(a => a.IsEnabled == enabled);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(a => new AlarmDto(a.Id, a.CreatedAt, a.AlarmId, a.IsActive, a.IsEnabled,
                a.IsAutoResumeEnabled, a.IsStopCondition))
            .ToListAsync(ct);

        return new PagedResult<AlarmDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<PagedResult<DetectorDto>> GetDetectorsAsync(DateTime since, ApiDetectorKind[] detectorIds,
        bool? isEnabled, PageRequest page, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var q = db.DetectorHistory.AsNoTracking().Where(d => d.CreatedAt >= since);
        if (detectorIds.Length > 0)
            q = q.Where(d => detectorIds.Contains(d.DetectorId));

        if (isEnabled is { } enabled)
            q = q.Where(d => d.IsEnabled == enabled);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(d => new DetectorDto(d.Id, d.CreatedAt, d.DetectorId, d.IsActive, d.IsEnabled))
            .ToListAsync(ct);

        return new PagedResult<DetectorDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<PagedResult<EmergencyDto>> GetEmergenciesAsync(DateTime since, ApiEventKind[] kinds,
        ApiEmergencyStopReason[] stopReasons, ApiEmergencyResumeReason[] resumeReasons, PageRequest page,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var q = db.EmergencyStopHistory.AsNoTracking().Where(r => r.OccurredAt >= since);
        if (kinds.Length > 0)
            q = q.Where(r => kinds.Contains(r.Kind));

        // Only one of these is ever populated (a code is meaningless without a kind), but they are applied
        // independently so the store stays honest about what it was asked for.
        if (stopReasons.Length > 0)
            q = q.Where(r => r.StopReasonId != null && stopReasons.Contains(r.StopReasonId.Value));

        if (resumeReasons.Length > 0)
            q = q.Where(r => r.ResumeReasonId != null && resumeReasons.Contains(r.ResumeReasonId.Value));

        var total = await q.CountAsync(ct);

        // ActiveAlarms is stored as JSON, which cannot be deserialized inside the query, so the rows are
        // projected to their columns first and turned into DTOs in memory.
        var rows = await q
            .OrderByDescending(r => r.OccurredAt).ThenByDescending(r => r.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(r => new
            {
                r.Id,
                r.OccurredAt,
                r.Kind,
                r.StopReasonId,
                r.ResumeReasonId,
                r.ReasonText,
                r.ActiveAlarms,
                r.NotificationSentAt
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new EmergencyDto(r.Id, r.OccurredAt, r.Kind, r.StopReasonId, r.ResumeReasonId,
                r.ReasonText, ParseActiveAlarms(r.ActiveAlarms), r.NotificationSentAt))
            .ToList();

        return new PagedResult<EmergencyDto>(items, page.Page, page.PageSize, total);
    }

    // Null (a resume row) stays null on the wire. A row written by an older build, or hand-edited, could hold
    // something unparseable -- report no alarms rather than failing the whole page for it.
    private IReadOnlyList<ApiAlarmKind>? ParseActiveAlarms(string? json)
    {
        if (json is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<ApiAlarmKind>>(json) ?? [];
        }
        catch (JsonException)
        {
            LogUnreadableActiveAlarms(json);
            return [];
        }
    }

    public async Task<IReadOnlyList<AnimalDto>> GetAnimalsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Animals
            .AsNoTracking()
            .OrderBy(a => a.Name).ThenBy(a => a.Identifier)
            .Select(a => new AnimalDto(a.Identifier, a.Name, a.CreatedAt, a.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task RegisterAnimalAsync(string identifier, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        if (await db.Animals.AnyAsync(a => a.Identifier == identifier, ct))
            return;

        db.Animals.Add(new Entities.Animal { Identifier = identifier });
        await db.SaveChangesAsync(ct);
    }

    public async Task SetAnimalNameAsync(string identifier, string name, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var animal = await db.Animals.FirstOrDefaultAsync(a => a.Identifier == identifier, ct);

        if (animal is null)
            db.Animals.Add(new Entities.Animal { Identifier = identifier, Name = name });
        else if (animal.Name != name)
            animal.Name = name;
        else
            return;   // already correct; nothing to write

        await db.SaveChangesAsync(ct);
    }
}
