using System.Text.Json;
using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Models;
using AutoTrainer.Api.Options;

namespace AutoTrainer.Api.Emergency;

public class EmergencyWorker : BackgroundService
{
    private readonly IEmergencyQueue _queue;

    private readonly AutotrainerDevice _device;

    private readonly MessageQueueOptions _options;

    private readonly ILogger<EmergencyWorker> _logger;

    private IAmazonSimpleNotificationService? _snsClient;

    private static readonly JsonSerializerOptions messageSerializerOptions = JsonDefaults.CamelCase;

    public EmergencyWorker(IEmergencyQueue queue, AutotrainerDevice device, IOptions<MessageQueueOptions> options, ILogger<EmergencyWorker> logger)
    {
        (_queue, _device, _options, _logger) = (queue, device, options.Value, logger);
    }

    public override void Dispose()
    {
        _snsClient?.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var apiEvent = await _queue.DequeueAsync(stoppingToken);

                await HandleEmergencyAsync(apiEvent);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing emergency event");
            }
        }

        _logger.LogInformation("EmergencyWorker exiting at: {time}", DateTimeOffset.Now);
    }

    private async Task HandleEmergencyAsync(ApiEvent apiEvent)
    {
        if (!_options.SnS.IsConfigured)
        {
            _logger.LogWarning("AWS credentials are not set. Skipping SNS publish.");
            return;
        }

        JsonElement? context = apiEvent.Context is JsonElement c ? c : null;

        var reason = context?.Deserialize<ApiReasonPayload>(messageSerializerOptions)?.Reason;

        var ts = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(apiEvent.When * 1000));

        var subject = $"{_options.SnS.DeviceId} Emergency {(apiEvent.Kind == ApiEventKind.EmergencyStop ? "Stop" : "Resume")} (cause: {reason ?? "unspecified"})";

        var alertMessage = $"Device {_options.SnS.DeviceId} Emergency {(apiEvent.Kind == ApiEventKind.EmergencyStop ? "Stop" : "Resume")} at {ts} due to: {reason ?? "unspecified"}\n\n\n";

        var alarms = _device.GetActiveAlarms();

        if (alarms.Count == 0)
        {
            alertMessage += $"No active alarm found.\n";
        }
        else
        {
            alertMessage += $"Alarm Conditions:\n";

            foreach (var alarm in alarms)
            {
                alertMessage += $"\t- Alarm {alarm.AlarmId} is active\n";
            }
        }

        var detectors = _device.GetActiveDetectors();

        if (detectors.Count == 0)
        {
            alertMessage += $"No active detectors found.\n";
        }
        else
        {
            alertMessage += $"Detector Conditions:\n";

            foreach (var detector in detectors)
            {
                alertMessage += $"\t- Detector {detector.DetectorId} is active\n";
            }
        }

        _snsClient ??= new AmazonSimpleNotificationServiceClient(
            new BasicAWSCredentials(_options.SnS.AccessKeyId, _options.SnS.AccessKey), RegionEndpoint.USEast2);

        await PublishToTopicAsync(_snsClient, _options.SnS.TopicArn, subject, alertMessage);
    }

    private async Task PublishToTopicAsync(IAmazonSimpleNotificationService client, string topicArn, string subject, string messageText)
    {
        try
        {
            var request = new PublishRequest
            {
                TopicArn = topicArn,
                Subject = subject,
                Message = messageText,
            };

            var response = await client.PublishAsync(request);

            _logger.LogInformation("Successfully published message ID: {id}", response.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError("{message}", ex.Message);
        }
    }
}
