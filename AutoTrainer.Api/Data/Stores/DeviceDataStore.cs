using AutoTrainer.Api.ApiTypes;
using Entities = AutoTrainer.Api.Data.Entities;

namespace AutoTrainer.Api.Data.Stores;

public interface IDeviceDataStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task AddSystemConfigurationAsync(ApiSystemConfiguration config, CancellationToken ct = default);
    Task AddAlarmHistoryAsync(ApiAlarmStatus status, CancellationToken ct = default);
    Task AddDetectorHistoryAsync(ApiDetectorStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<Entities.AlarmHistory>> GetAlarmHistoryAsync(DateTime since, CancellationToken ct = default);
    Task<IReadOnlyList<Entities.DetectorHistory>> GetDetectorHistoryAsync(DateTime since, CancellationToken ct = default);
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

    public async Task<IReadOnlyList<Entities.AlarmHistory>> GetAlarmHistoryAsync(DateTime since, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.AlarmHistory
            .AsNoTracking()
            .Where(a => a.CreatedAt >= since)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Entities.DetectorHistory>> GetDetectorHistoryAsync(DateTime since, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.DetectorHistory
            .AsNoTracking()
            .Where(d => d.CreatedAt >= since)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
    }
}
