using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Models;

namespace AutoTrainer.Api.Endpoints;

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var device = app.MapGroup("/device");

        device.MapGet("", (AutotrainerDevice device) => device);
        device.MapGet("/cage/latest", GetLatestCageImage);
        device.MapGet("/alarms", GetAlarmHistory);
        device.MapGet("/detectors", GetDetectorHistory);

        return app;
    }

    private static async Task<IResult> GetAlarmHistory(string? within, IDeviceDataStore store, CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return Results.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        var rows = await store.GetAlarmHistoryAsync(DateTime.UtcNow - window, ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> GetDetectorHistory(string? within, IDeviceDataStore store, CancellationToken ct)
    {
        if (!TimeWindow.TryParse(within, out var window))
            return Results.BadRequest($"Invalid time window '{within}'. {TimeWindow.Usage}");

        var rows = await store.GetDetectorHistoryAsync(DateTime.UtcNow - window, ct);

        return Results.Ok(rows);
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
