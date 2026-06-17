using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Emergency;
using AutoTrainer.Api.Hub;
using AutoTrainer.Api.Models;
using AutoTrainer.Api.Options;

namespace AutoTrainer.Api.MessageQueue;

public partial class MessageSubscriptionWorker : BackgroundService
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


        LogQueueConnection(options.Value.Connection);

        _connection = options.Value.Connection;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Run(() =>
        {
            LogStartingRuntime(DateTimeOffset.Now);

            using var runtime = new NetMQRuntime();

            runtime.Run(stoppingToken, SubscribeAsync(stoppingToken));

            LogEndingRuntime(DateTimeOffset.Now);
        }, stoppingToken);

        LogWorkerEnding(DateTimeOffset.Now);
    }

    private async Task SubscribeAsync(CancellationToken stoppingToken)
    {
        using var subscriber = new SubscriberSocket();

        subscriber.Connect(_connection);

        subscriber.Subscribe("");

        LogSubscribed();

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
                                    LogFromPublisher(topic, apiEvent.Kind);
                                    await _emergencyQueue.EnqueueAsync(apiEvent);
                                }
                                else
                                {
                                    LogDeserializeFailed();
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
                            LogUnhandledTopic(topic);
                            break;
                    }

                }
            }
            catch (Exception ex)
            {
                LogSubscriptionError(ex.Message);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Message queue connection: {connection}")]
    private partial void LogQueueConnection(string connection);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting runtime: {time}")]
    private partial void LogStartingRuntime(DateTimeOffset time);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ending runtime: {time}")]
    private partial void LogEndingRuntime(DateTimeOffset time);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker ending at: {time}")]
    private partial void LogWorkerEnding(DateTimeOffset time);

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscribed")]
    private partial void LogSubscribed();

    [LoggerMessage(Level = LogLevel.Information, Message = "From Publisher| {topic}: {kind}")]
    private partial void LogFromPublisher(ApiTopic topic, ApiEventKind kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize emergency event")]
    private partial void LogDeserializeFailed();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unhandled topic {topic}")]
    private partial void LogUnhandledTopic(ApiTopic topic);

    [LoggerMessage(Level = LogLevel.Error, Message = "{message}")]
    private partial void LogSubscriptionError(string message);
}
