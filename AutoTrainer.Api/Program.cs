using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Data;
using AutoTrainer.Api.Emergency;
using AutoTrainer.Api.Hub;
using AutoTrainer.Api.MessageQueue;
using AutoTrainer.Api.Models;
using AutoTrainer.Api.Options;
using AutoTrainer.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "Autotrainer_");

builder.Services.AddSignalR();

builder.Services.AddSingleton<AutotrainerDevice>();
builder.Services.AddSingleton<ICommandTaskQueue, CommandTaskQueue>();
builder.Services.AddSingleton<IEmergencyQueue, EmergencyTaskQueue>();
builder.Services.AddSingleton<DataWatcherService>();
builder.Services.AddHostedService<MessageSubscriptionWorker>();
builder.Services.AddHostedService<CommandQueueWorker>();
builder.Services.AddHostedService<EmergencyWorker>();
builder.Services.AddHostedService<ServiceHeartbeatWorker>();
builder.Services.AddHostedService<DeviceUpdateWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DataWatcherService>());

// This is not secure for something other than an internal network, however that is the only deployment condition that we intend to support.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        builder =>
        {
            builder
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});


builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
});

builder.Services.Configure<AutoTrainerOptions>(builder.Configuration.GetSection(AutoTrainerOptions.AutoTrainer));
builder.Services.Configure<MessageQueueOptions>(builder.Configuration.GetSection(AutoTrainerOptions.AutoTrainer).GetSection(MessageQueueOptions.MessageQueue));
builder.Services.Configure<CommandQueueOptions>(builder.Configuration.GetSection(AutoTrainerOptions.AutoTrainer).GetSection(CommandQueueOptions.CommandQueue));

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
    {
        context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
    }

    await next();
});

app.UseCors();

app.MapGet("/device", (AutotrainerDevice device) => device);

app.MapGet("/device/cage/latest", (AutotrainerDevice device) =>
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
});

app.MapHub<MessageHub>("/messages");

app.MapGet("/health", () => Results.Ok());

app.Run();
