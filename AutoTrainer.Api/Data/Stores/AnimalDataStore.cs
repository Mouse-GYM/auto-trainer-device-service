using System.Collections.Concurrent;
using AutoTrainer.Api.ApiTypes;
using Entities = AutoTrainer.Api.Data.Entities;

namespace AutoTrainer.Api.Data.Stores;

public interface IAnimalDataStore
{
    Task<bool> EnsureAnimalDatabaseAsync(string identifier, CancellationToken ct = default);
    Task AddAnimalInfoAsync(ApiAnimalStatus status, CancellationToken ct = default);
    Task AddReachEventHistoryAsync(string identifier, IReadOnlyCollection<ReachEvent> reachEvents, CancellationToken ct = default);
}

public partial class AnimalDataStore(IAnimalDbContextFactory factory, ILogger<AnimalDataStore> logger) : IAnimalDataStore
{
    private readonly ILogger<AnimalDataStore> _logger = logger;

    private readonly ConcurrentDictionary<string, bool> _initialized = new();

    public async Task<bool> EnsureAnimalDatabaseAsync(string identifier, CancellationToken ct = default)
    {
        if (!SqliteStorage.IsValidIdentifier(identifier))
        {
            LogRejectedIdentifier(identifier);
            return false;
        }

        if (_initialized.ContainsKey(identifier))
            return true;

        await using var db = factory.Create(identifier);
        await db.Database.MigrateAsync(ct);
        _initialized[identifier] = true;

        LogAnimalDatabaseReady(identifier);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected invalid animal identifier '{identifier}'.")]
    private partial void LogRejectedIdentifier(string identifier);

    [LoggerMessage(Level = LogLevel.Information, Message = "Animal database ready for '{identifier}'.")]
    private partial void LogAnimalDatabaseReady(string identifier);

    public async Task AddAnimalInfoAsync(ApiAnimalStatus status, CancellationToken ct = default)
    {
        if (!await EnsureAnimalDatabaseAsync(status.Identifier, ct))
            return;

        await using var db = factory.Create(status.Identifier);

        db.AnimalInfo.Add(new Entities.AnimalInfo
        {
            Identifier = status.Identifier,
            Name = status.Name ?? "",
            DcsSendX = status.DcsSendX,
            DcsSendY = status.DcsSendY,
            DcsSendZ = status.DcsSendZ,
            TargetYLimit = status.TargetYLimit
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task AddReachEventHistoryAsync(string identifier, IReadOnlyCollection<ReachEvent> reachEvents, CancellationToken ct = default)
    {
        if (reachEvents.Count == 0)
            return;

        if (!await EnsureAnimalDatabaseAsync(identifier, ct))
            return;

        await using var db = factory.Create(identifier);

        foreach (var reachEvent in reachEvents)
        {
            db.ReachEventHistory.Add(new Entities.ReachEventHistory
            {
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
}