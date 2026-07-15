using AutoTrainer.Api.Data;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Models;

namespace AutoTrainer.Api.Endpoints;

public static class AnimalEndpoints
{
    public static IEndpointRouteBuilder MapAnimalEndpoints(this IEndpointRouteBuilder app)
    {
        var animals = app.MapGroup("/animals").LogRequestsInDevelopment(app).LogClientDisconnects();

        animals.MapGet("", GetAnimals);
        animals.MapGet("/{id}", GetAnimalDetail);

        // Singular: the currently-selected device animal (optionally overridden by ?animal=).
        var animal = app.MapGroup("/animal").LogRequestsInDevelopment(app).LogClientDisconnects();
        animal.MapGet("/reachstatus", GetAnimalReachStatus);

        return app;
    }

    // Latest Total and current-device-day Day reach-status snapshots for the selected animal. The day portion is
    // null when the device day is unknown or has no row yet; the total is null before the first total row exists.
    internal static async Task<IResult> GetAnimalReachStatus(string? animal, AutotrainerDevice device,
        IAnimalDataStore store, ISqliteStorage storage, CancellationToken ct)
    {
        var res = AnimalResolution.Resolve(animal, device.Animal?.Identifier, storage);
        if (res.Error is { } error) return error;
        if (res.Empty) return TypedResults.NotFound();

        var status = await store.GetReachStatusAsync(res.Identifier!, device.CurrentDeviceDay, ct);
        return TypedResults.Ok(status);
    }

    // The flat registry list: a single indexed query on the device database, no per-animal file access.
    internal static async Task<IResult> GetAnimals(IDeviceDataStore store, CancellationToken ct)
    {
        var animals = await store.GetAnimalsAsync(ct);
        return TypedResults.Ok(animals);
    }

    // Single-animal detail from that animal's own database. The path id is always "provided", so resolution
    // yields 400 (invalid) / 404 (no database), never the empty-selection branch.
    internal static async Task<IResult> GetAnimalDetail(string id, IAnimalDataStore store, ISqliteStorage storage,
        CancellationToken ct)
    {
        var res = AnimalResolution.Resolve(id, selectedIdentifier: null, storage);
        if (res.Error is { } error) return error;

        var detail = await store.GetAnimalDetailAsync(res.Identifier!, ct);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }
}
