using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Data;
using AutoTrainer.Api.Endpoints;
using AutoTrainer.Api.Data.Stores;
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

builder.Services.AddSingleton<ISqliteStorage, SqliteStorage>();
builder.Services.AddDbContextFactory<DeviceDbContext>((sp, options) =>
{
    var storage = sp.GetRequiredService<ISqliteStorage>();
    options.UseSqlite(storage.DeviceConnectionString);
});
builder.Services.AddSingleton<IAnimalDbContextFactory, AnimalDbContextFactory>();
builder.Services.AddSingleton<IDeviceDataStore, DeviceDataStore>();
builder.Services.AddSingleton<IAnimalDataStore, AnimalDataStore>();

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
builder.Services.Configure<DataOptions>(builder.Configuration.GetSection(AutoTrainerOptions.AutoTrainer).GetSection(DataOptions.Data));

var app = builder.Build();

var storage = app.Services.GetRequiredService<ISqliteStorage>();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

if (!storage.EnsureDirectories(out var storageError))
{
    startupLogger.StorageDirectoriesInitFailed(storage.RootPath, storageError);
    return 1;
}

if (File.Exists(storage.DeviceDatabasePath))
{
    startupLogger.DeviceDatabaseFound(storage.DeviceDatabasePath);
}
else
{
    startupLogger.DeviceDatabaseNotFound(storage.DeviceDatabasePath);
}

try
{
    await app.Services.GetRequiredService<IDeviceDataStore>().InitializeAsync();
}
catch (Exception ex)
{
    startupLogger.DeviceDatabaseMigrationFailed(ex);
    return 1;
}

app.Use(async (context, next) =>
{
    if (context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
    {
        context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
    }

    await next();
});

app.UseCors();

app.MapDeviceEndpoints();

app.MapHub<MessageHub>("/messages");

app.MapGet("/health", () => Results.Ok());

app.Run();

return 0;

internal static partial class ProgramLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Unable to initialize SQLite storage directories at {root}: {error}")]
    public static partial void StorageDirectoriesInitFailed(this ILogger logger, string root, string? error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Device database found at {path}")]
    public static partial void DeviceDatabaseFound(this ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Device database not found at {path}; it will be created")]
    public static partial void DeviceDatabaseNotFound(this ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create/migrate the device database")]
    public static partial void DeviceDatabaseMigrationFailed(this ILogger logger, Exception ex);
}
