using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Emergency;
using AutoTrainer.Api.Hub;
using AutoTrainer.Api.Models;
using AutoTrainer.Api.Options;

namespace AutoTrainer.Api.MessageQueue;

public class MessageSubscriptionWorker : BackgroundService
{
    private readonly string _connection;

    private readonly IHubContext<MessageHub, IMessageHub> _hubContext;

    private readonly AutotrainerDevice _device;

    private readonly ICommandTaskQueue _commandQueue;

    private readonly IEmergencyQueue _emergencyQueue;

    private readonly ILogger<MessageSubscriptionWorker> _logger;

    private static readonly JsonSerializerOptions messageSerializerOptions = JsonDefaults.CamelCase;

    public MessageSubscriptionWorker(IHubContext<MessageHub, IMessageHub> hubContext, AutotrainerDevice device, ICommandTaskQueue commandQueue, IEmergencyQueue emergencyQueue, IOptions<MessageQueueOptions> options, ILogger<MessageSubscriptionWorker> logger)
    {
        (_hubContext, _device, _commandQueue, _emergencyQueue, _logger) = (hubContext, device, commandQueue, emergencyQueue, logger);


        _logger.LogInformation("Message queue connection: {connection}", options.Value.Connection);

        _connection = options.Value.Connection;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Run(() =>
        {
            _logger.LogInformation("Starting runtime: {time}", DateTimeOffset.Now);

            using var runtime = new NetMQRuntime();

            runtime.Run(stoppingToken, SubscribeAsync(stoppingToken));

            _logger.LogInformation("Ending runtime: {time}", DateTimeOffset.Now);
        }, stoppingToken);

        _logger.LogInformation("Worker ending at: {time}", DateTimeOffset.Now);
    }

    private async Task SubscribeAsync(CancellationToken stoppingToken)
    {
        using var subscriber = new SubscriberSocket();

        subscriber.Connect(_connection);

        subscriber.Subscribe("");

        _logger.LogInformation("Subscribed");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = await subscriber.ReceiveMultipartMessageAsync(cancellationToken: stoppingToken);

                if (message != null && message.FrameCount == 2)
                {
                    var topic = (ApiTopic)BitConverter.ToUInt32(message[0].AsSpan());

                    var data = message[1].ToByteArray();

                    switch (topic)
                    {
                        case ApiTopic.Heartbeat:
                            {
                                var heartbeat = JsonSerializer.Deserialize<ApiHeartBeat>(data, messageSerializerOptions);

                                if (heartbeat != null)
                                {
                                    _device.OnHeartbeat(heartbeat);
                                }
                            }
                            break;
                        case ApiTopic.Emergency:
                            {
                                var apiEvent = JsonSerializer.Deserialize<ApiEvent>(data, messageSerializerOptions);
                                if (apiEvent != null)
                                {
                                    _logger.LogInformation("From Publisher| {topic}: {kind}", topic, apiEvent.Kind);
                                    await _emergencyQueue.EnqueueAsync(apiEvent);
                                }
                                else
                                {
                                    _logger.LogWarning("Failed to deserialize emergency event");
                                }
                            }
                            break;
                        case ApiTopic.Event:
                            {
                                var apiEvent = JsonSerializer.Deserialize<ApiEvent>(data, messageSerializerOptions);

                                if (apiEvent != null)
                                {
                                    _device.OnApiEvent(apiEvent);
                                }
                            }
                            break;
                        case ApiTopic.CommandResult:
                            {
                                var response = JsonSerializer.Deserialize<ApiCommandRequestResponse>(data, messageSerializerOptions);

                                _device.OnCommandResponse(response);
                            }
                            break;
                        default:
                            _logger.LogWarning("Unhandled topic {topic}", topic);
                            break;
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError("{message}", ex.Message);
            }
        }
    }
}
