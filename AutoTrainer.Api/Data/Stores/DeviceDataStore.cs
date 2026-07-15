using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Endpoints;
using Entities = AutoTrainer.Api.Data.Entities;

namespace AutoTrainer.Api.Data.Stores;

public interface IDeviceDataStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task AddSystemConfigurationAsync(ApiSystemConfiguration config, CancellationToken ct = default);
    Task AddAlarmHistoryAsync(ApiAlarmStatus status, CancellationToken ct = default);
    Task AddDetectorHistoryAsync(ApiDetectorStatus status, CancellationToken ct = default);

    // Paged, time-windowed, type-filtered reads for the REST query API. An empty alarmIds/detectorIds means
    // "no type filter". Newest-first by observation time.
    Task<PagedResult<AlarmDto>> GetAlarmsAsync(DateTime since, ApiAlarmKind[] alarmIds, PageRequest page,
        CancellationToken ct = default);
    Task<PagedResult<DetectorDto>> GetDetectorsAsync(DateTime since, ApiDetectorKind[] detectorIds, PageRequest page,
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

    public async Task<PagedResult<AlarmDto>> GetAlarmsAsync(DateTime since, ApiAlarmKind[] alarmIds,
        PageRequest page, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var q = db.AlarmHistory.AsNoTracking().Where(a => a.CreatedAt >= since);
        if (alarmIds.Length > 0)
            q = q.Where(a => alarmIds.Contains(a.AlarmId));   // filter on the canonical AlarmId

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
        PageRequest page, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var q = db.DetectorHistory.AsNoTracking().Where(d => d.CreatedAt >= since);
        if (detectorIds.Length > 0)
            q = q.Where(d => detectorIds.Contains(d.DetectorId));

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(d => new DetectorDto(d.Id, d.CreatedAt, d.DetectorId, d.IsActive, d.IsEnabled))
            .ToListAsync(ct);

        return new PagedResult<DetectorDto>(items, page.Page, page.PageSize, total);
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
