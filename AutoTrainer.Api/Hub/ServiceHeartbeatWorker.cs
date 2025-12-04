using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Hub;

public class ServiceHeartbeatWorker : BackgroundService
{
    private readonly IHubContext<MessageHub, IMessageHub> _hubContext;

    private readonly ILogger<ServiceHeartbeatWorker> _logger;

    public ServiceHeartbeatWorker(IHubContext<MessageHub, IMessageHub> hubContext, ILogger<ServiceHeartbeatWorker> logger)
    {
        (_hubContext, _logger) = (hubContext, logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var heartbeat = new ApiHeartBeat
            {
                Identifier = "autotrainer-device-service",
                Version = "1",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await _hubContext.Clients.All.ServiceHeartbeat(heartbeat);

            _logger.LogDebug("Service heartbeat sent");

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
