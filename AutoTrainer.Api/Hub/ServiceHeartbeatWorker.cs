using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Hub;

public partial class ServiceHeartbeatWorker : BackgroundService
{
    private readonly IHubContext<MessageHub, IMessageHub> _hubContext;

    private readonly ILogger<ServiceHeartbeatWorker> _logger;

    public ServiceHeartbeatWorker(IHubContext<MessageHub, IMessageHub> hubContext, ILogger<ServiceHeartbeatWorker> logger)
    {
        (_hubContext, _logger) = (hubContext, logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
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

                LogHeartbeatSent();

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown: the stopping token cancelled the delay.
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Service heartbeat sent")]
    private partial void LogHeartbeatSent();
}
