using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using AutoTrainer.Api.Options;
using Microsoft.Extensions.Logging;

namespace AutoTrainer.Api.MessageQueue;

public class MessageSubscriptionWorker : BackgroundService
{
    private readonly string _connection;

    private readonly ILogger<MessageSubscriptionWorker> _logger;

    private readonly IHubContext<MessageHub, IMessageHub> _hubContext;

    private const string _defaultConnection = "tcp://127.0.0.1:5556";

    public MessageSubscriptionWorker(ILogger<MessageSubscriptionWorker> logger, IHubContext<MessageHub, IMessageHub> hubContext, IConfiguration configuration)
    {
        _logger = logger;

        _hubContext = hubContext;

        _connection = configuration.GetSection(AutoTrainerOptions.AutoTrainer)?.GetSection(QueueOptions.MessageQueue)?.GetValue<string>(QueueOptions.ConnectionKey) ?? _defaultConnection;
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

        await Task.Delay(1000, stoppingToken);
    }

    private async Task SubscribeAsync(CancellationToken stoppingToken)
    {
        using var subscriber = new SubscriberSocket();

        subscriber.Connect(_connection);

        subscriber.Subscribe("");

        _logger.LogInformation("Subscribed");

        while (!stoppingToken.IsCancellationRequested)
        {
            var message = await subscriber.ReceiveMultipartMessageAsync(cancellationToken: stoppingToken);

            if (message != null && message.FrameCount == 2)
            {
                var topic = BitConverter.ToUInt32(message[0].AsSpan());

                var data = message[1].ConvertToString();

                var jsonObj = JsonSerializer.Deserialize<object>(data, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                _logger.LogInformation("From Publisher| {topic}: {msg}", topic, jsonObj);

                await _hubContext.Clients.All.ReceiveMessage($"{topic}", jsonObj);

                if ((ApiTopic)topic == ApiTopic.Emergency)
                {
                    _logger.LogInformation("Emergency topic");
                    // using var snsClient = new AmazonSimpleNotificationServiceClient(
                    //    new BasicAWSCredentials("", ""), RegionEndpoint.USEast2);
                    // await PublishToTopicAsync(snsClient, topicArn, data);
                }
            }
        }
    }

    string topicArn = "arn:aws:sns:us-east-2:178022192522:mouse-gym-standard-notifications";

    private async Task PublishToTopicAsync(IAmazonSimpleNotificationService client, string topicArn, string messageText)
    {
        try
        {
            var request = new PublishRequest
            {
                TopicArn = topicArn,
                Message = messageText,
            };

            var response = await client.PublishAsync(request);

            _logger.LogInformation("Successfully published message ID: {id}", response.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
        }
    }
}
