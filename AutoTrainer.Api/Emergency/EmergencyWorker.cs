using System.Text.Json;
using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Models;
using AutoTrainer.Api.Options;

namespace AutoTrainer.Api.Emergency;

public partial class EmergencyWorker : BackgroundService
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
                LogEmergencyError(ex);
            }
        }

        LogExiting(DateTimeOffset.Now);
    }

    // internal so tests can drive it directly, without the hosted-service loop or an SNS client.
    internal async Task HandleEmergencyAsync(ApiEvent apiEvent)
    {
        if (!_options.SnS.IsConfigured)
        {
            LogCredentialsNotSet();
            return;
        }

        // The payload type is a function of the kind alone; ApiEventPayloadMap is the authority for which is
        // which. Deserializing both kinds as ApiReasonPayload would discard the stop's active_alarms.
        var payload = DeserializePayload(apiEvent);

        var (subject, alertMessage) = BuildAlert(
            apiEvent, payload, _device.Configuration.DeviceId,
            _device.GetActiveAlarms(), _device.GetActiveDetectors());

        _snsClient ??= new AmazonSimpleNotificationServiceClient(
            new BasicAWSCredentials(_options.SnS.AccessKeyId, _options.SnS.AccessKey), RegionEndpoint.USEast2);

        if (await PublishToTopicAsync(_snsClient, _options.SnS.TopicArn, subject, alertMessage))
        {
            // Not a direct store call: the stamp goes through the device's update channel so it stays
            // serialized with the history insert and works in either arrival order.
            _device.OnNotificationSent(apiEvent, DateTime.UtcNow);
        }
    }

    // Composes the operator alert. Pure and internal so the alarm-source behavior is unit-testable without an
    // SNS client. liveAlarms/liveDetectors are the device's current snapshot, used only where the event
    // payload carries nothing better.
    internal static (string Subject, string Body) BuildAlert(ApiEvent apiEvent, object? payload, string deviceId,
        IReadOnlyList<Alarm> liveAlarms, IReadOnlyList<Detector> liveDetectors)
    {
        var isStop = apiEvent.Kind == ApiEventKind.EmergencyStop;

        var stopPayload = payload as ApiEmergencyStopPayload;

        // The raw producer string, not the enum name: the alert is human-facing, and the raw string carries
        // the "alarm-monitor:" token suffix the enum deliberately discards.
        var reason = stopPayload?.Reason ?? (payload as ApiReasonPayload)?.Reason;

        var ts = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(apiEvent.When * 1000));

        var what = isStop ? "Stop" : "Resume";

        var subject = $"{deviceId} Emergency {what} (cause: {reason ?? "unspecified"})";

        var body = $"Device {deviceId} Emergency {what} at {ts} due to: {reason ?? "unspecified"}\n\n\n";

        // A stop carries the alarms that were engaged when it fired; report those rather than whatever
        // happens to be active now. A resume payload has no such field, so the live snapshot is all there is
        // -- the two are labelled differently so a reader can tell them apart.
        if (stopPayload is not null)
        {
            var active = stopPayload.ActiveAlarms;

            if (active.Count == 0)
            {
                body += "No alarm was active at the emergency stop.\n";
            }
            else
            {
                body += "Alarm Conditions (active at the emergency stop):\n";

                foreach (var alarm in active)
                {
                    body += $"\t- Alarm {alarm} is active\n";
                }
            }
        }
        else if (liveAlarms.Count == 0)
        {
            body += "No alarm is currently active.\n";
        }
        else
        {
            body += "Alarm Conditions (currently active):\n";

            foreach (var alarm in liveAlarms)
            {
                body += $"\t- Alarm {alarm.AlarmId} is active\n";
            }
        }

        // No payload equivalent on either kind, so this is always the current snapshot.
        if (liveDetectors.Count == 0)
        {
            body += "No detector is currently active.\n";
        }
        else
        {
            body += "Detector Conditions (currently active):\n";

            foreach (var detector in liveDetectors)
            {
                body += $"\t- Detector {detector.DetectorId} is active\n";
            }
        }

        return (subject, body);
    }

    // Mirrors AutotrainerDevice.DeserializePayload: the kind is the only discriminator, and a malformed
    // payload must not take down the alert.
    private object? DeserializePayload(ApiEvent apiEvent)
    {
        if (apiEvent.Context is not JsonElement element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var type = ApiEventPayloadMap.PayloadType(apiEvent.Kind);

        if (type is null)
            return null;

        try
        {
            return element.Deserialize(type, messageSerializerOptions);
        }
        catch (JsonException ex)
        {
            LogPublishError(ex.Message);
            return null;
        }
    }

    // Returns whether the publish succeeded, so only a delivered notification is recorded as sent.
    // internal virtual is the test seam: tests override it rather than constructing an SNS client, which
    // leaves the production client creation and lifetime untouched.
    internal virtual async Task<bool> PublishToTopicAsync(IAmazonSimpleNotificationService client, string topicArn, string subject, string messageText)
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

            LogPublished(response.MessageId);

            return true;
        }
        catch (Exception ex)
        {
            LogPublishError(ex.Message);

            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing emergency event")]
    private partial void LogEmergencyError(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "EmergencyWorker exiting at: {time}")]
    private partial void LogExiting(DateTimeOffset time);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AWS credentials are not set. Skipping SNS publish.")]
    private partial void LogCredentialsNotSet();

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully published message ID: {id}")]
    private partial void LogPublished(string id);

    [LoggerMessage(Level = LogLevel.Error, Message = "{message}")]
    private partial void LogPublishError(string message);
}
