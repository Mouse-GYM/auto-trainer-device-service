using AutoTrainer.Api.Contracts;
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
        // The :int constraint means a non-numeric note id fails at routing rather than at binding.
        animals.MapGet("/{id}/behaviornotes", GetBehaviorNotes);
        animals.MapPost("/{id}/behaviornotes", CreateBehaviorNote);
        animals.MapPatch("/{id}/behaviornotes/{noteId:int}", EditBehaviorNote);
        animals.MapDelete("/{id}/behaviornotes/{noteId:int}", DeleteBehaviorNote);

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

    // The behavior-note log for one animal. Existence is a registry-row check, not AnimalResolution.Resolve: that
    // helper gates on the per-animal database file, which an animal can lack while still being registered (and
    // vice versa). Only the identifier's format is validated here; the 404 comes from the store. Every handler
    // below follows the same two-stage check.
    internal static async Task<IResult> GetBehaviorNotes(string id, string? sort, int? page, int? pageSize,
        IDeviceDataStore store, CancellationToken ct)
    {
        if (!SqliteStorage.IsValidIdentifier(id))
            return TypedResults.BadRequest($"Invalid animal identifier '{id}'.");

        var sortReq = SortRequest.From(sort);
        if (!NoteSort.IsSupported(sortReq))
            return TypedResults.BadRequest($"Unsupported sort field '{sortReq.Field}'. Supported: {NoteSort.CreatedAt}.");

        var pr = PageRequest.From(page, pageSize);
        var result = await store.GetBehaviorNotesAsync(id, sortReq, pr, ct);

        // Null is no registry row; a registered animal with an empty log is an empty page, not a 404.
        return result is null ? NoAnimal(id) : TypedResults.Ok(result);
    }

    internal static async Task<IResult> CreateBehaviorNote(string id, NoteWrite? write, IDeviceDataStore store,
        AutotrainerDevice device, CancellationToken ct)
    {
        if (!SqliteStorage.IsValidIdentifier(id))
            return TypedResults.BadRequest($"Invalid animal identifier '{id}'.");

        if (!NotesText.TryAccept(write?.Body, out var body, out var err))
            return TypedResults.BadRequest(err);

        var result = await store.AddBehaviorNoteAsync(id, body, ct);

        if (result.Outcome == NoteWriteOutcome.NoSuchAnimal)
            return NoAnimal(id);

        // After the awaited write: the queued action re-reads the registry, so enqueueing it first would race
        // the commit. It is fire-and-forget, so this response goes out before the broadcast does.
        device.OnAnimalNoteChanged(id, result.Note!.Id, NoteChangeKind.Created);

        return TypedResults.Created($"/animals/{id}/behaviornotes/{result.Note.Id}", result.Note);
    }

    internal static async Task<IResult> EditBehaviorNote(string id, int noteId, NoteWrite? write,
        IDeviceDataStore store, AutotrainerDevice device, CancellationToken ct)
    {
        if (!SqliteStorage.IsValidIdentifier(id))
            return TypedResults.BadRequest($"Invalid animal identifier '{id}'.");

        if (!NotesText.TryAccept(write?.Body, out var body, out var err))
            return TypedResults.BadRequest(err);

        var result = await store.EditBehaviorNoteAsync(id, noteId, body, ct);

        if (result.Outcome == NoteWriteOutcome.NoSuchAnimal)
            return NoAnimal(id);

        if (result.Outcome == NoteWriteOutcome.NoSuchNote)
            return NoNote(id, noteId);

        device.OnAnimalNoteChanged(id, result.Note!.Id, NoteChangeKind.Edited);

        return TypedResults.Ok(result.Note);
    }

    internal static async Task<IResult> DeleteBehaviorNote(string id, int noteId, IDeviceDataStore store,
        AutotrainerDevice device, CancellationToken ct)
    {
        if (!SqliteStorage.IsValidIdentifier(id))
            return TypedResults.BadRequest($"Invalid animal identifier '{id}'.");

        var result = await store.DeleteBehaviorNoteAsync(id, noteId, ct);

        if (result.Outcome == NoteWriteOutcome.NoSuchAnimal)
            return NoAnimal(id);

        if (result.Outcome == NoteWriteOutcome.NoSuchNote)
            return NoNote(id, noteId);

        // The route value: a successful delete returns no note to read an id from.
        device.OnAnimalNoteChanged(id, noteId, NoteChangeKind.Deleted);

        return TypedResults.NoContent();
    }

    // The two 404s must read differently -- which is why the store distinguishes them.
    private static IResult NoAnimal(string id) =>
        TypedResults.NotFound($"No animal '{id}' in the device registry.");

    private static IResult NoNote(string id, int noteId) =>
        TypedResults.NotFound($"No note {noteId} for animal '{id}'.");
}
