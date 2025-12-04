using AutoTrainer.Api.Models;

namespace AutoTrainer.Api.Workers;

public class DeviceUpdateWorker(AutotrainerDevice device, ILogger<DeviceUpdateWorker> logger) : BackgroundService
{
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
                logger.LogError(ex, "Error processing device update");
            }
        }

        logger.LogInformation("DeviceUpdateWorker exiting at: {time}", DateTimeOffset.Now);
    }
}
