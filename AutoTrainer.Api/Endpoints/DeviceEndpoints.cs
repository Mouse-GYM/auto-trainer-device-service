using AutoTrainer.Api.ApiTypes;
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
        device.MapGet("/events", GetApiEventHistory);

        return app;
    }

    internal static async Task<IResult> GetAlarms(string? within, string[]? alarmId, int? page, int? pageSize,
        IDeviceDataStore store, CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        if (!EnumFilter.TryParse<ApiAlarmKind>(alarmId, out var alarmIds, out var err))
            return TypedResults.BadRequest(err);

        var pr = PageRequest.From(page, pageSize);
        var result = await store.GetAlarmsAsync(window.StartUtc(DateTime.UtcNow), [.. alarmIds], pr, ct);
        return TypedResults.Ok(result);
    }

    internal static async Task<IResult> GetDetectors(string? within, string[]? detectorId, int? page, int? pageSize,
        IDeviceDataStore store, CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return TypedResults.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        if (!EnumFilter.TryParse<ApiDetectorKind>(detectorId, out var detectorIds, out var err))
            return TypedResults.BadRequest(err);

        var pr = PageRequest.From(page, pageSize);
        var result = await store.GetDetectorsAsync(window.StartUtc(DateTime.UtcNow), [.. detectorIds], pr, ct);
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
