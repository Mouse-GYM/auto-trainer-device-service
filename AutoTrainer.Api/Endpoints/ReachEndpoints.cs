using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Data;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Models;

namespace AutoTrainer.Api.Endpoints;

public static class ReachEndpoints
{
    public static IEndpointRouteBuilder MapReachEndpoints(this IEndpointRouteBuilder app)
    {
        // Animal data, not device data: relocated from /device/reaches. `method`/`outcome` are integer codes.
        // "Raw" because an unqualified reach means a right-hand hand event; these are the analysis's own
        // reach rows, for callers that specifically want them.
        app.MapGet("/raw-reaches", GetRawReaches).LogRequestsInDevelopment(app).LogClientDisconnects();
        app.MapGet("/raw-reaches/count", GetRawReachesCount).LogRequestsInDevelopment(app).LogClientDisconnects();

        // The other two lists of the same intertrial analysis, each its own table: hand-based events that are
        // not reaches, and events that are not hand-based. Same filters and shape as /raw-reaches. The reaches
        // a caller normally means are /hand-reaches?method=2.
        app.MapGet("/hand-reaches", GetHandReaches).LogRequestsInDevelopment(app).LogClientDisconnects();
        app.MapGet("/hand-reaches/count", GetHandReachesCount).LogRequestsInDevelopment(app).LogClientDisconnects();
        app.MapGet("/other-reaches", GetOtherReaches).LogRequestsInDevelopment(app).LogClientDisconnects();
        app.MapGet("/other-reaches/count", GetOtherReachesCount).LogRequestsInDevelopment(app).LogClientDisconnects();
        return app;
    }

    internal static async Task<IResult> GetRawReaches(string? within, string? animal, int[]? method, int[]? outcome,
        int? page, int? pageSize, AutotrainerDevice device, IAnimalDataStore store, ISqliteStorage storage,
        CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        var pr = PageRequest.From(page, pageSize);

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(PagedResult<ReachEventDto>.Empty(pr.Page, pr.PageSize));

        var result = await store.GetRawReachEventsAsync(res.Identifier!, window.StartUtc(DateTime.UtcNow),
            method ?? [], outcome ?? [], pr, ct);
        return TypedResults.Ok(result);
    }

    // Count analogue of GetRawReaches: same filters (within, animal, method, outcome), no paging.
    internal static async Task<IResult> GetRawReachesCount(string? within, string? animal, int[]? method,
        int[]? outcome, AutotrainerDevice device, IAnimalDataStore store, ISqliteStorage storage,
        CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(new CountDto(0));

        var count = await store.CountRawReachEventsAsync(res.Identifier!, window.StartUtc(DateTime.UtcNow),
            method ?? [], outcome ?? [], ct);
        return TypedResults.Ok(new CountDto(count));
    }

    // The four below are GetRawReaches/GetRawReachesCount against the other two tables. Spelled out rather than
    // parameterised on purpose: the three reach tables are free to diverge, and one shared handler is the kind
    // of coupling that would make diverging expensive.
    internal static async Task<IResult> GetHandReaches(string? within, string? animal, int[]? method,
        int[]? outcome, int? page, int? pageSize, AutotrainerDevice device, IAnimalDataStore store,
        ISqliteStorage storage, CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        var pr = PageRequest.From(page, pageSize);

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(PagedResult<ReachEventDto>.Empty(pr.Page, pr.PageSize));

        var result = await store.GetHandReachEventsAsync(res.Identifier!, window.StartUtc(DateTime.UtcNow),
            method ?? [], outcome ?? [], pr, ct);
        return TypedResults.Ok(result);
    }

    internal static async Task<IResult> GetHandReachesCount(string? within, string? animal, int[]? method,
        int[]? outcome, AutotrainerDevice device, IAnimalDataStore store, ISqliteStorage storage,
        CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(new CountDto(0));

        var count = await store.CountHandReachEventsAsync(res.Identifier!, window.StartUtc(DateTime.UtcNow),
            method ?? [], outcome ?? [], ct);
        return TypedResults.Ok(new CountDto(count));
    }

    internal static async Task<IResult> GetOtherReaches(string? within, string? animal, int[]? method,
        int[]? outcome, int? page, int? pageSize, AutotrainerDevice device, IAnimalDataStore store,
        ISqliteStorage storage, CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        var pr = PageRequest.From(page, pageSize);

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(PagedResult<ReachEventDto>.Empty(pr.Page, pr.PageSize));

        var result = await store.GetOtherReachEventsAsync(res.Identifier!, window.StartUtc(DateTime.UtcNow),
            method ?? [], outcome ?? [], pr, ct);
        return TypedResults.Ok(result);
    }

    internal static async Task<IResult> GetOtherReachesCount(string? within, string? animal, int[]? method,
        int[]? outcome, AutotrainerDevice device, IAnimalDataStore store, ISqliteStorage storage,
        CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(new CountDto(0));

        var count = await store.CountOtherReachEventsAsync(res.Identifier!, window.StartUtc(DateTime.UtcNow),
            method ?? [], outcome ?? [], ct);
        return TypedResults.Ok(new CountDto(count));
    }
}
