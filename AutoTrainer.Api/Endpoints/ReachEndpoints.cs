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
        app.MapGet("/reaches", GetReaches).LogRequestsInDevelopment(app).LogClientDisconnects();
        app.MapGet("/reaches/count", GetReachesCount).LogRequestsInDevelopment(app).LogClientDisconnects();
        return app;
    }

    internal static async Task<IResult> GetReaches(string? within, string? animal, int[]? method, int[]? outcome,
        int? page, int? pageSize, AutotrainerDevice device, IAnimalDataStore store, ISqliteStorage storage,
        CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        var pr = PageRequest.From(page, pageSize);

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(PagedResult<ReachEventDto>.Empty(pr.Page, pr.PageSize));

        var result = await store.GetReachEventsAsync(res.Identifier!, window.StartUtc(DateTime.UtcNow),
            method ?? [], outcome ?? [], pr, ct);
        return TypedResults.Ok(result);
    }

    // Count analogue of GetReaches: same filters (within, animal, method, outcome), no paging.
    internal static async Task<IResult> GetReachesCount(string? within, string? animal, int[]? method,
        int[]? outcome, AutotrainerDevice device, IAnimalDataStore store, ISqliteStorage storage,
        CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.Ok(new CountDto(0));

        var count = await store.CountReachEventsAsync(res.Identifier!, window.StartUtc(DateTime.UtcNow),
            method ?? [], outcome ?? [], ct);
        return TypedResults.Ok(new CountDto(count));
    }
}
