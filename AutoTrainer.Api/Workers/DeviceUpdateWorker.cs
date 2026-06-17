using AutoTrainer.Api.Models;

namespace AutoTrainer.Api.Workers;

public partial class DeviceUpdateWorker(AutotrainerDevice device, ILogger<DeviceUpdateWorker> logger) : BackgroundService
{
    private readonly ILogger<DeviceUpdateWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var action in device.UpdateReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                LogUpdateError(ex);
            }
        }

        LogExiting(DateTimeOffset.Now);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing device update")]
    private partial void LogUpdateError(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "DeviceUpdateWorker exiting at: {time}")]
    private partial void LogExiting(DateTimeOffset time);
}
