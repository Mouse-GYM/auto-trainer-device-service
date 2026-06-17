using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Data.Stores;
using AutoTrainer.Api.Hub;

namespace AutoTrainer.Api.Models;

public partial class AutotrainerDevice
{
    private const int LatestWebImageThrottleSeconds = 2;

    private readonly IHubContext<MessageHub, IMessageHub> _hubContext;

    private readonly ICommandTaskQueue _commandQueue;

    private readonly ILogger<AutotrainerDevice> _logger;

    private readonly IDeviceDataStore _deviceStore;

    private readonly IAnimalDataStore _animalStore;

    private readonly Channel<Func<Task>> _updateChannel = Channel.CreateUnbounded<Func<Task>>();

    private DateTime _lastWebImageBroadcast = DateTime.MinValue;

    public AutotrainerDevice(ICommandTaskQueue commandQueue, IHubContext<MessageHub, IMessageHub> hubContext, ILogger<AutotrainerDevice> logger, IDeviceDataStore deviceStore, IAnimalDataStore animalStore)
    {
        _commandQueue = commandQueue;
        _hubContext = hubContext;
        _logger = logger;
        _deviceStore = deviceStore;
        _animalStore = animalStore;
    }

    public ChannelReader<Func<Task>> UpdateReader => _updateChannel.Reader;

    public SystemConfiguration Configuration { get; } = new();

    public ApiApplicationMode ApplicationMode { get; private set; } = ApiApplicationMode.Idle;

    public ApiTrainingMode TrainingMode { get; private set; } = ApiTrainingMode.Manual;

    public SystemState SystemState { get; private set; } = SystemState.Cage;

    public DateTimeOffset? LastSeen { get; private set; }

    public List<Alarm> Alarms { get; private set; } = [];

    public List<Detector> Detectors { get; private set; } = [];

    public List<Alarm> GetActiveAlarms() => [.. Alarms.Where(a => a.IsEnabled && a.IsActive)];

    public List<Detector> GetActiveDetectors() => [.. Detectors.Where(a => a.IsEnabled && a.IsActive)];

    public PelletDevice PelletDevice { get; } = new();

    public TunnelDevice TunnelDevice { get; } = new();

    public Analysis Analysis { get; } = new();

    public Behavior Behavior { get; } = new();

    public List<ReachEvent> ReachEvents { get; private set; } = [];

    public Animal? Animal { get; private set; }

    public string? DeviceDataPath { get; private set; }

    public string? TrialPath { get; private set; }

    public string? WebImagesPath { get; private set; }

    public string? LatestWebImage { get; private set; }

    public void OnHeartbeat(ApiHeartBeat heartbeat)
    {
        _updateChannel.Writer.TryWrite(async () =>
        {
            var when = DateTimeOffset.FromUnixTimeSeconds((long)Math.Round(heartbeat.Timestamp));

            LogHeartbeat(heartbeat.Identifier, when);

            var shouldRequestConfig = LastSeen is null || DateTimeOffset.UtcNow - LastSeen > TimeSpan.FromMinutes(1);

            LastSeen = when;

            if (shouldRequestConfig)
            {
                LogConfigurationOutOfDate();

                await _commandQueue.EnqueueAsync(new ApiCommandRequest(ApiCommandKind.GetConfiguration));
            }

            await _hubContext.Clients.All.Heartbeat(heartbeat);
        });
    }

    public void OnApiEvent(ApiEvent apiEvent)
    {
        _updateChannel.Writer.TryWrite(async () =>
        {
            JsonElement? context = apiEvent.Context is JsonElement c ? c : null;

            LogEvent(apiEvent.Kind);

            switch (apiEvent.Kind)
            {
                case ApiEventKind.AlarmChanged:
                    {
                        var ctx = context?.Deserialize<ApiAlarmStatus>(s_JsonOptions);

                        if (ctx != null)
                        {
                            OnAlarmChanged(ctx.Value);

                            try
                            {
                                await _deviceStore.AddAlarmHistoryAsync(ctx.Value);
                            }
                            catch (Exception ex)
                            {
                                LogPersistAlarmHistoryFailed(ex, ctx.Value.AlarmId);
                            }
                        }
                        break;
                    }
                case ApiEventKind.DetectorChanged:
                    {
                        var ctx = context?.Deserialize<ApiDetectorStatus>(s_JsonOptions);

                        if (ctx != null)
                        {
                            OnDetectorChanged(ctx.Value);

                            try
                            {
                                await _deviceStore.AddDetectorHistoryAsync(ctx.Value);
                            }
                            catch (Exception ex)
                            {
                                LogPersistDetectorHistoryFailed(ex, ctx.Value.DetectorId);
                            }
                        }
                        break;
                    }
                case ApiEventKind.TunnelEnter:
                    {
                        SystemState = SystemState.Tunnel;

                        await _hubContext.Clients.All.SystemStateChanged(SystemState);

                        break;
                    }
                case ApiEventKind.TunnelExit:
                    {
                        SystemState = SystemState.Cage;

                        await _hubContext.Clients.All.SystemStateChanged(SystemState);

                        break;
                    }
                case ApiEventKind.LoadCellEngagedChanged:
                    {
                        var ctx = context?.Deserialize<ApiEngagedChangedPayload>(s_JsonOptions);

                        if (ctx != null)
                        {
                            Analysis.LoadCellEngaged = ctx.IsEngaged;
                            await _hubContext.Clients.All.AnalysisChanged(Analysis);
                        }
                        break;
                    }
                case ApiEventKind.HeadbarPressureEngagedChanged:
                    {
                        var ctx = context?.Deserialize<ApiEngagedChangedPayload>(s_JsonOptions);

                        if (ctx != null)
                        {
                            Analysis.HeadbarPressureEngaged = ctx.IsEngaged;
                            await _hubContext.Clients.All.AnalysisChanged(Analysis);
                        }
                        break;
                    }
                case ApiEventKind.HeadfixBaselineChanged:
                    {
                        var ctx = context?.Deserialize<ApiHeadfixBaselineChangedPayload>(s_JsonOptions);

                        if (ctx != null)
                        {
                            Behavior.BaselineMagnetIntensity = ctx.Baseline;
                            await _hubContext.Clients.All.BehaviorChanged(Behavior);
                        }
                        break;
                    }
                case ApiEventKind.HeadfixLoadCellEnabledChanged:
                    {
                        var ctx = context?.Deserialize<ApiEngagedChangedPayload>(s_JsonOptions);

                        if (ctx != null)
                        {
                            Behavior.LoadCellEnabled = ctx.IsEngaged;
                            await _hubContext.Clients.All.BehaviorChanged(Behavior);
                        }
                        break;
                    }
                case ApiEventKind.PelletSendBegin:
                case ApiEventKind.PelletCoverBegin:
                case ApiEventKind.PelletReleaseBegin:
                case ApiEventKind.PelletRetractBegin:
                case ApiEventKind.PelletHomeBegin:
                case ApiEventKind.PelletHomeReset:
                case ApiEventKind.PelletDriftReset:
                    {
                        var ctx = context?.Deserialize<ApiContextPayload>(s_JsonOptions);

                        if (ctx != null)
                        {
                            PelletDevice.UpdateLastCommand(apiEvent);
                            await _hubContext.Clients.All.PelletDeviceChanged(PelletDevice);
                        }
                        break;
                    }
                case ApiEventKind.SystemStatus:
                    {
                        var ctx = context?.Deserialize<ApiSystemStatus>(s_JsonOptions);

                        if (ctx != null)
                        {
                            OnSystemStatusChanged(ctx);
                        }
                        break;
                    }
                case ApiEventKind.AnimalSelected:
                case ApiEventKind.AnimalUpdated:
                    {
                        var ctx = context?.Deserialize<ApiAnimalStatus>(s_JsonOptions);

                        if (ctx is { } status)
                        {
                            if (string.IsNullOrWhiteSpace(status.Identifier))
                            {
                                LogMissingAnimalIdentifier(apiEvent.Kind);
                            }
                            else
                            {
                                try
                                {
                                    await _animalStore.AddAnimalInfoAsync(status);
                                }
                                catch (Exception ex)
                                {
                                    LogPersistAnimalFailed(ex, status.Identifier);
                                }
                            }
                        }
                        break;
                    }
                case ApiEventKind.TrialReachEvents:
                    {
                        var ctx = context?.Deserialize<ApiTrialReachEventsPayload>(s_JsonOptions);

                        if (ctx != null)
                        {
                            OnReachEventsChanged(ctx.TrialReachEvents);

                            var identifier = Animal?.Identifier;

                            if (string.IsNullOrWhiteSpace(identifier))
                            {
                                LogMissingAnimalIdentifier(apiEvent.Kind);
                            }
                            else if (ctx.TrialReachEvents.Count > 0)
                            {
                                try
                                {
                                    await _animalStore.AddReachEventHistoryAsync(identifier, ctx.TrialReachEvents);
                                }
                                catch (Exception ex)
                                {
                                    LogPersistReachEventsFailed(ex, identifier);
                                }
                            }
                        }
                        break;
                    }
            }

            await _hubContext.Clients.All.EventReceived(apiEvent);
        });
    }

    public void OnCommandResponse(ApiCommandRequestResponse response)
    {
        _updateChannel.Writer.TryWrite(async () =>
        {
            await _hubContext.Clients.All.CommandResponseReceived(response);

            LogCommandResponse(response.Command, response.Result);

            if (response.Result != ApiCommandRequestResult.Success)
            {
                return;
            }

            switch (response.Command)
            {
                case ApiCommandKind.GetConfiguration:
                    {
                        var config = DeserializeData<ApiSystemConfiguration>(response.Data);
                        if (config != null)
                        {
                            OnSystemConfigurationChanged(config);

                            try
                            {
                                await _deviceStore.AddSystemConfigurationAsync(config);
                            }
                            catch (Exception ex)
                            {
                                LogPersistConfigurationFailed(ex);
                            }

                            await _commandQueue.EnqueueAsync(new ApiCommandRequest(ApiCommandKind.GetStatus));
                        }
                        break;
                    }
                case ApiCommandKind.GetStatus:
                    {
                        var status = DeserializeData<ApiSystemStatus>(response.Data);
                        if (status != null)
                        {
                            OnSystemStatusChanged(status);
                        }
                        break;
                    }
            }
        });
    }

    public void OnDeviceDataPathChanged(string? path)
    {
        _updateChannel.Writer.TryWrite(async () =>
        {
            DeviceDataPath = path;
            LogDeviceDataPath(path ?? "(none)");
            await _hubContext.Clients.All.DeviceDataPath(path);
        });
    }

    public void OnTrialPathChanged(string? path)
    {
        _updateChannel.Writer.TryWrite(async () =>
        {
            TrialPath = path;
            LogTrialPath(path ?? "(none)");
            await _hubContext.Clients.All.TrialPath(path);
        });
    }

    public void OnWebImagesPathChanged(string? path)
    {
        _updateChannel.Writer.TryWrite(async () =>
        {
            WebImagesPath = path;
            LogWebImagesPath(path ?? "(none)");
            await _hubContext.Clients.All.WebImagesPath(path);
        });
    }

    public void OnLatestWebImageChanged(string? path)
    {
        _updateChannel.Writer.TryWrite(async () =>
        {
            LatestWebImage = path;

            var now = DateTime.UtcNow;

            if ((now - _lastWebImageBroadcast).TotalSeconds < LatestWebImageThrottleSeconds)
                return;

            _lastWebImageBroadcast = now;
            LogLatestWebImage(path ?? "(none)");

            await _hubContext.Clients.All.LatestWebImage(path);
        });
    }

    private void OnAlarmChanged(ApiAlarmStatus status)
    {
        var alarm = MapAlarm(status);

        var existing = Alarms.Find(a => a.AlarmId == alarm.AlarmId);

        if (existing is null)
        {
            LogAlarmAdded(alarm.AlarmId, alarm.IsActive, alarm.IsEnabled);
            Alarms = [.. Alarms, alarm];
        }
        else
        {
            LogAlarmUpdated(alarm.AlarmId, alarm.IsActive, alarm.IsEnabled);
            Alarms = [.. Alarms.Select(a => a.AlarmId == alarm.AlarmId ? alarm : a)];
        }

        _hubContext.Clients.All.AlarmChanged(alarm);
    }

    private void OnDetectorChanged(ApiDetectorStatus status)
    {
        var detector = MapDetector(status);
        var existing = Detectors.Find(d => d.DetectorId == detector.DetectorId);

        if (existing is null)
        {
            LogDetectorAdded(detector.DetectorId, detector.IsActive, detector.IsEnabled);
            Detectors = [.. Detectors, detector];
        }
        else
        {
            LogDetectorUpdated(detector.DetectorId, detector.IsActive, detector.IsEnabled);
            Detectors = [.. Detectors.Select(d => d.DetectorId == detector.DetectorId ? detector : d)];
        }

        _hubContext.Clients.All.DetectorChanged(detector);
    }

    private void OnApplicationModeChanged(ApiApplicationMode mode)
    {
        ApplicationMode = mode;
        _hubContext.Clients.All.ApplicationModeChanged(mode);
    }

    private void OnTrainingModeChanged(ApiTrainingMode mode)
    {
        TrainingMode = mode;
        _hubContext.Clients.All.TrainingModeChanged(mode);
    }

    private void OnPelletDeviceChanged(ApiPelletStatus status)
    {
        PelletDevice.ApplyStatus(status);
        _hubContext.Clients.All.PelletDeviceChanged(PelletDevice);
    }

    private void OnTunnelDeviceChanged(ApiTunnelStatus status)
    {
        TunnelDevice.ApplyStatus(status);
        _hubContext.Clients.All.TunnelDeviceChanged(TunnelDevice);
    }

    private void OnBehaviorChanged(ApiBehaviorStatus status)
    {
        Behavior.ApplyStatus(status);
        _hubContext.Clients.All.BehaviorChanged(Behavior);
    }

    private void OnReachEventsChanged(List<ReachEvent> reachEvents)
    {
        ReachEvents = reachEvents;
        _hubContext.Clients.All.ReachEventsChanged(ReachEvents);
    }

    private void OnAnimalChanged(ApiAnimalStatus? status)
    {
        if (status is { } s)
        {
            var animal = Animal ?? new Animal();
            animal.ApplyStatus(s);
            Animal = animal;
        }
        else
        {
            Animal = null;
        }

        _hubContext.Clients.All.AnimalChanged(Animal);
    }

    private void OnAlarmsChanged(List<ApiAlarmStatus> statuses)
    {
        Alarms = [.. statuses.Select(MapAlarm)];
        _hubContext.Clients.All.AlarmsChanged(Alarms);
    }

    private void OnDetectorsChanged(List<ApiDetectorStatus> statuses)
    {
        Detectors = [.. statuses.Select(MapDetector)];
        _hubContext.Clients.All.DetectorsChanged(Detectors);
    }

    private static Alarm MapAlarm(ApiAlarmStatus s) => new()
    {
        AlarmId = s.AlarmId != 0 ? s.AlarmId : s.DetectorId,
        IsActive = s.IsActive,
        IsEnabled = s.IsEnabled,
        IsAutoResumeEnabled = s.IsAutoResumeEnabled,
        IsStopCondition = s.IsStopCondition
    };

    private static Detector MapDetector(ApiDetectorStatus s) => new()
    {
        DetectorId = s.DetectorId,
        IsActive = s.IsActive,
        IsEnabled = s.IsEnabled
    };

    private void OnSystemConfigurationChanged(ApiSystemConfiguration config)
    {
        Configuration.ApplyStatus(config);

        LogDataLocationUpdated(Configuration.DataLocation);

        _hubContext.Clients.All.SystemConfigurationChanged(config);
    }

    private void OnSystemStatusChanged(ApiSystemStatus status)
    {
        OnApplicationModeChanged(status.ApplicationMode);
        OnTrainingModeChanged(status.TrainingMode);
        OnAnimalChanged(status.Animal);
        OnAlarmsChanged(status.Alarms);
        OnDetectorsChanged(status.Detectors);
        OnPelletDeviceChanged(status.PelletDevice);
        OnTunnelDeviceChanged(status.TunnelDevice);
        OnBehaviorChanged(status.Behavior);

        LogSystemStatus(status);
    }

    private static readonly JsonSerializerOptions s_JsonOptions = JsonDefaults.CamelCase;

    private static T? DeserializeData<T>(IDictionary<string, object>? data) where T : class
    {
        if (data is null)
        {
            return null;
        }

        var element = JsonSerializer.SerializeToElement(data, s_JsonOptions);

        return element.Deserialize<T>(s_JsonOptions);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Heartbeat: {identifier} {when}")]
    private partial void LogHeartbeat(string identifier, DateTimeOffset when);

    [LoggerMessage(Level = LogLevel.Information, Message = "System configuration is missing or out of date.  Requesting update.")]
    private partial void LogConfigurationOutOfDate();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Event: {evet}")]
    private partial void LogEvent(ApiEventKind evet);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{kind} received with no animal identifier; skipping database select/create.")]
    private partial void LogMissingAnimalIdentifier(ApiEventKind kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist animal info for {identifier}")]
    private partial void LogPersistAnimalFailed(Exception ex, string identifier);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist reach events for {identifier}")]
    private partial void LogPersistReachEventsFailed(Exception ex, string identifier);

    [LoggerMessage(Level = LogLevel.Information, Message = "Command response {command} {result}")]
    private partial void LogCommandResponse(ApiCommandKind command, ApiCommandRequestResult result);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist system configuration")]
    private partial void LogPersistConfigurationFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist alarm history for {alarmId}")]
    private partial void LogPersistAlarmHistoryFailed(Exception ex, ApiAlarmKind alarmId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist detector history for {detectorId}")]
    private partial void LogPersistDetectorHistoryFailed(Exception ex, ApiDetectorKind detectorId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Device data path: {path}")]
    private partial void LogDeviceDataPath(string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trial path: {path}")]
    private partial void LogTrialPath(string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Web images path: {path}")]
    private partial void LogWebImagesPath(string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Latest web image: {path}")]
    private partial void LogLatestWebImage(string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Alarm {alarmId} added: active={isActive}, enabled={isEnabled}")]
    private partial void LogAlarmAdded(ApiAlarmKind alarmId, bool isActive, bool isEnabled);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Alarm {alarmId} updated: active={isActive}, enabled={isEnabled}")]
    private partial void LogAlarmUpdated(ApiAlarmKind alarmId, bool isActive, bool isEnabled);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Detector {detectorId} added: active={isActive}, enabled={isEnabled}")]
    private partial void LogDetectorAdded(ApiDetectorKind detectorId, bool isActive, bool isEnabled);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Detector {detectorId} updated: active={isActive}, enabled={isEnabled}")]
    private partial void LogDetectorUpdated(ApiDetectorKind detectorId, bool isActive, bool isEnabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Data location updated to {location}")]
    private partial void LogDataLocationUpdated(string location);

    [LoggerMessage(Level = LogLevel.Information, Message = "System Status: {status}")]
    private partial void LogSystemStatus(ApiSystemStatus status);
}
