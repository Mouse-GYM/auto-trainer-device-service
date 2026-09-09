using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Models;

namespace AutoTrainer.Api.Endpoints;

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var device = app.MapGroup("/device").LogRequestsInDevelopment(app).LogClientDisconnects();

        device.MapGet("", (AutotrainerDevice device) => device);
        device.MapGet("/cage/latest", GetLatestCageImage);
        device.MapGet("/alarms", GetAlarms);
        device.MapGet("/detectors", GetDetectors);
        device.MapGet("/emergencies", GetEmergencies);
        device.MapGet("/events", GetApiEventHistory);
        // The :int constraint means a non-numeric id fails at routing rather than at binding.
        device.MapGet("/systemnotes", GetSystemNotes);
        device.MapPost("/systemnotes", CreateSystemNote);
        device.MapPatch("/systemnotes/{noteId:int}", EditSystemNote);
        device.MapDelete("/systemnotes/{noteId:int}", DeleteSystemNote);

        return app;
    }

    // isEnabled is omitted for "either". These are history rows, so it selects observations recorded while the
    // alarm was enabled or disabled -- not what is enabled right now, which GET /device reports.
    internal static async Task<IResult> GetAlarms(string? within, string[]? alarmId, bool? isEnabled, int? page,
        int? pageSize, IDeviceDataStore store, CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        if (!EnumFilter.TryParse<ApiAlarmKind>(alarmId, out var alarmIds, out var err))
            return TypedResults.BadRequest(err);

        var pr = PageRequest.From(page, pageSize);
        var result = await store.GetAlarmsAsync(window.StartUtc(DateTime.UtcNow), [.. alarmIds], isEnabled, pr, ct);
        return TypedResults.Ok(result);
    }

    internal static async Task<IResult> GetDetectors(string? within, string[]? detectorId, bool? isEnabled,
        int? page, int? pageSize, IDeviceDataStore store, CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        if (!EnumFilter.TryParse<ApiDetectorKind>(detectorId, out var detectorIds, out var err))
            return TypedResults.BadRequest(err);

        var pr = PageRequest.From(page, pageSize);
        var result = await store.GetDetectorsAsync(window.StartUtc(DateTime.UtcNow), [.. detectorIds], isEnabled, pr, ct);
        return TypedResults.Ok(result);
    }

    // Only the two emergency kinds ever appear in this table. Anything else is rejected rather than silently
    // returning an empty page, since EnumFilter happily parses any ApiEventKind name.
    private static readonly ApiEventKind[] s_EmergencyKinds =
        [ApiEventKind.EmergencyStop, ApiEventKind.EmergencyResume];

    // `code` filters on the reason code, but a code only identifies a reason once the kind is known: the stop
    // and resume reasons are separate enums that deliberately share integer values (201 is UserButton in
    // both, 101 is AlarmMonitor for a stop and AlarmMonitorResumed for a resume). So `code` applies only when
    // `kind` narrows to exactly one direction. When the request returns both kinds, `code` is ignored
    // outright -- not validated, not applied -- because there is no enum to interpret it against.
    internal static async Task<IResult> GetEmergencies(string? within, string[]? kind, string[]? code,
        int? page, int? pageSize, IDeviceDataStore store, CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        if (!EnumFilter.TryParse<ApiEventKind>(kind, out var kinds, out var err))
            return TypedResults.BadRequest(err);

        if (kinds.Except(s_EmergencyKinds).ToArray() is { Length: > 0 } unsupported)
            return TypedResults.BadRequest(
                $"Unsupported kind '{string.Join(", ", unsupported)}'. Use {nameof(ApiEventKind.EmergencyStop)} " +
                $"or {nameof(ApiEventKind.EmergencyResume)}.");

        // Asking for both kinds explicitly is the same as asking for all of them.
        var only = kinds.Distinct().ToArray() is [var single] ? single : (ApiEventKind?)null;

        ApiEmergencyStopReason[] stopReasons = [];
        ApiEmergencyResumeReason[] resumeReasons = [];

        if (only == ApiEventKind.EmergencyStop)
        {
            if (!EnumFilter.TryParse<ApiEmergencyStopReason>(code, out var parsed, out var codeError))
                return TypedResults.BadRequest(codeError);

            stopReasons = [.. parsed];
        }
        else if (only == ApiEventKind.EmergencyResume)
        {
            if (!EnumFilter.TryParse<ApiEmergencyResumeReason>(code, out var parsed, out var codeError))
                return TypedResults.BadRequest(codeError);

            resumeReasons = [.. parsed];
        }

        var pr = PageRequest.From(page, pageSize);
        var result = await store.GetEmergenciesAsync(window.StartUtc(DateTime.UtcNow), [.. kinds],
            stopReasons, resumeReasons, pr, ct);
        return TypedResults.Ok(result);
    }

    // The request contract exists; the backing store does not yet — ApiEvents are not persisted. Persisting
    // them (device-level vs per-animal, table shape) is a separate future task, so the data step returns 404.
    // The full request is still parsed and validated so the contract is exercised and documented.
    internal static IResult GetApiEventHistory(string? within, string[]? kind, int? page, int? pageSize)
    {
        if (!TimeWindow.TryParse(within, out _))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        if (!EnumFilter.TryParse<ApiEventKind>(kind, out _, out var err))
            return TypedResults.BadRequest(err);

        _ = PageRequest.From(page, pageSize);
        return TypedResults.NotFound("ApiEvent history is not yet persisted.");
    }

    internal static async Task<IResult> GetSystemNotes(string? sort, int? page, int? pageSize,
        IDeviceDataStore store, CancellationToken ct)
    {
        var sortReq = SortRequest.From(sort);
        if (!NoteSort.IsSupported(sortReq))
            return TypedResults.BadRequest($"Unsupported sort field '{sortReq.Field}'. Supported: {NoteSort.CreatedAt}.");

        var pr = PageRequest.From(page, pageSize);
        var result = await store.GetSystemNotesAsync(sortReq, pr, ct);
        return TypedResults.Ok(result);
    }

    internal static async Task<IResult> CreateSystemNote(NoteWrite? write, IDeviceDataStore store,
        AutotrainerDevice device, CancellationToken ct)
    {
        if (!NotesText.TryAccept(write?.Body, out var body, out var err))
            return TypedResults.BadRequest(err);

        var note = await store.AddSystemNoteAsync(body, ct);

        // After the awaited write: the queued action re-reads the store, so enqueueing it first would race
        // the commit.
        device.OnSystemNoteChanged(note.Id, NoteChangeKind.Created);

        return TypedResults.Created($"/device/systemnotes/{note.Id}", note);
    }

    internal static async Task<IResult> EditSystemNote(int noteId, NoteWrite? write, IDeviceDataStore store,
        AutotrainerDevice device, CancellationToken ct)
    {
        if (!NotesText.TryAccept(write?.Body, out var body, out var err))
            return TypedResults.BadRequest(err);

        var result = await store.EditSystemNoteAsync(noteId, body, ct);

        if (result.Outcome == NoteWriteOutcome.NoSuchNote)
            return TypedResults.NotFound($"No system note {noteId}.");

        device.OnSystemNoteChanged(result.Note!.Id, NoteChangeKind.Edited);

        return TypedResults.Ok(result.Note);
    }

    internal static async Task<IResult> DeleteSystemNote(int noteId, IDeviceDataStore store,
        AutotrainerDevice device, CancellationToken ct)
    {
        var result = await store.DeleteSystemNoteAsync(noteId, ct);

        if (result.Outcome == NoteWriteOutcome.NoSuchNote)
            return TypedResults.NotFound($"No system note {noteId}.");

        // The route value: a successful delete returns no note to read an id from.
        device.OnSystemNoteChanged(noteId, NoteChangeKind.Deleted);

        return TypedResults.NoContent();
    }

    private static IResult GetLatestCageImage(AutotrainerDevice device)
    {
        if (string.IsNullOrEmpty(device.LatestWebImage) || !File.Exists(device.LatestWebImage))
            return Results.NotFound();

        var fullPath = Path.GetFullPath(device.LatestWebImage);

        if (!fullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return Results.NotFound();

        const int maxWidth = 480;

        using var original = SKBitmap.Decode(fullPath);

        if (original is null)
            return Results.StatusCode(500);

        if (original.Width <= maxWidth)
            return Results.File(fullPath, "image/png");

        float scale = (float)maxWidth / original.Width;
        int newHeight = (int)(original.Height * scale);

        using var resized = original.Resize(new SKImageInfo(maxWidth, newHeight), new SKSamplingOptions(SKFilterMode.Linear));
        using var image = SKImage.FromBitmap(resized);
        var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);

        return Results.Bytes(data.ToArray(), "image/jpeg");
    }
}
