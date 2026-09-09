using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.CommandQueue;
using AutoTrainer.Api.Contracts;
using AutoTrainer.Api.Data;
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

    // Correlates an emergency history row with the notification published for the same event. The insert and
    // the stamp arrive from independent workers in either order, so whichever runs first leaves a note here
    // for the other: _emergencyRowIds carries an inserted row's id for a stamp still to come, _pendingStamps
    // carries a send time for an insert still to come.
    //
    // Both are read and written ONLY from inside actions queued on _updateChannel, which DeviceUpdateWorker
    // drains one at a time -- so access is single-threaded and needs no locking. Never touch them from the
    // enqueueing methods themselves; that code runs on the caller's thread.
    // Each entry carries when it was added, so eviction can prefer entries that have plainly lost their
    // counterpart over ones that may still be in flight.
    private readonly Dictionary<(ApiEventKind Kind, long Index), (int Value, DateTime AddedAt)> _emergencyRowIds = [];
    private readonly Dictionary<(ApiEventKind Kind, long Index), (DateTime Value, DateTime AddedAt)> _pendingStamps = [];

    // Not a cap -- the size at which a sweep for expired entries becomes worth doing. Entries are only ever
    // removed by age, never to satisfy a count.
    private const int EmergencyCorrelationSweepThreshold = 32;

    // A counterpart arrives within one SNS round trip plus a channel drain -- seconds. Anything older than
    // this has lost its counterpart for good (the emergency queue dropped the event, or the event-topic copy
    // never arrived). Deliberately generous: evicting a live entry orphans a delivered notification, while
    // keeping a dead one costs a dictionary slot.
    private static readonly TimeSpan EmergencyCorrelationTimeout = TimeSpan.FromMinutes(5);

    // Running 5-day reach-status total for the selected animal (surfaced as Animal.ReachStatus5Day). Seeded from
    // the day table on selection and re-seeded on a DayPath day rollover; the current day's counts are updated in
    // memory from reach-status events. _fiveDayId is the animal the running total currently belongs to.
    private FiveDayReachStatus _fiveDay;
    private string? _fiveDayId;

    // Registry notes for the selected animal (surfaced as Animal.TrainerNotes/BehaviorNote). One read per animal,
    // keyed by identifier so a selection change invalidates it; StampAndBroadcastAnimal is synchronous and on a hot
    // path, so it must never read the database itself. _animalNotesId is the animal the cached notes belong to.
    private AnimalNotes _animalNotes = AnimalNotes.Empty;
    private string? _animalNotesId;

    // Day attribution for day*CountChanged events (which carry no day). The attribution day is the newer of the
    // most recent systemStatus DayPath day and the most recent DayStarted day, so a new day's counts are recorded
    // against the new day even before the next systemStatus arrives. Unlike the 5-day total (DayPath only), this
    // must respect DayStarted: a mis-attributed per-day row is persisted and never corrected.
    private DateOnly? _lastDayPathDay;
    private DateOnly? _lastDayStartedDay;

    // The device's current day: the newer of the most recent systemStatus DayPath day and DayStarted day. Null
    // until either is seen. Drives day*CountChanged attribution and the /animal/reachstatus day portion.
    public DateOnly? CurrentDeviceDay =>
        _lastDayPathDay is { } p
            ? (_lastDayStartedDay is { } s && s > p ? s : p)
            : _lastDayStartedDay;

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

    // Loads the values this service owns and no message carries, so a restart does not publish an empty value and
    // then have it reappear. Called once from Program.cs before any hosted service starts -- nothing else is
    // touching the device yet, which is why this is the one place model state is set off the update channel.
    // Sets the value only; nothing is connected this early, and the first real broadcast follows the first
    // GetConfiguration response anyway.
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            Configuration.SystemNote = await _deviceStore.GetLatestSystemNoteAsync(ct);
        }
        catch (Exception ex)
        {
            // Descriptive text; starting without it beats not starting.
            LogSystemNotesLoadFailed(ex);
        }
    }

    // A note mutation has no api message behind it, so it needs its own way to reach the broadcasts. Queued on
    // the update channel like every other entry point, so Configuration is still only mutated on the worker.
    //
    // NoteChanged carries the mutation's identity and always goes out. The re-read that follows takes no value:
    // the caller's commit and this enqueue are separate steps, so two overlapping writes can reach the channel
    // in the opposite order to the commits. Re-reading here means whichever action runs last lands on the
    // durable value; applying a captured argument would leave the retained model disagreeing with the database
    // until the next restart. The note that changed may not be the newest one, so SystemConfigurationChanged --
    // which exists to carry live-model values -- fires only when the newest note actually differs.
    public void OnSystemNoteChanged(int noteId, NoteChangeKind kind)
    {
        _updateChannel.Writer.TryWrite(async () =>
        {
            await SendNoteChangedAsync(new NoteChangeDto(NoteScope.System, null, noteId, kind));

            NoteDto? latest;

            try
            {
                latest = await _deviceStore.GetLatestSystemNoteAsync();
            }
            catch (Exception ex)
            {
                // Keep the retained value and broadcast nothing further: a failed read must not blank the note or
                // publish a value nobody wrote. The notice above has already gone out, which is correct -- the
                // mutation did commit.
                LogSystemNotesLoadFailed(ex);
                return;
            }

            if (latest == Configuration.SystemNote)
                return;

            Configuration.SystemNote = latest;

            await _hubContext.Clients.All.SystemConfigurationChanged(Configuration);
        });
    }

    // Signals a behavior-note mutation and, when it touched the selected animal, re-reads and re-broadcasts it.
    // The NoteChanged notice is unconditional -- it is the only path by which a change to a non-selected animal
    // reaches clients, since AnimalChanged carries the live animal and nothing else.
    public void OnAnimalNoteChanged(string identifier, int noteId, NoteChangeKind kind)
    {
        _updateChannel.Writer.TryWrite(async () =>
        {
            await SendNoteChangedAsync(new NoteChangeDto(NoteScope.Behavior, identifier, noteId, kind));

            if (Animal is not { } animal || animal.Identifier != identifier)
                return;

            var before = _animalNotes.BehaviorNote;

            if (await RefreshAnimalNotesAsync(identifier, force: true) && before != _animalNotes.BehaviorNote)
                StampAndBroadcastAnimal();
        });
    }

    // Best-effort, and isolated: DeviceUpdateWorker catches per queued action, so a faulted send would otherwise
    // take the store re-read and the live-model update down with it, leaving the retained note stale after a
    // committed write.
    private async Task SendNoteChangedAsync(NoteChangeDto change)
    {
        try
        {
            await _hubContext.Clients.All.NoteChanged(change);
        }
        catch (Exception ex)
        {
            LogBroadcastNoteChangeFailed(ex);
        }
    }

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
            // The payload type is a function of the kind alone (an event carries no type tag), so it is
            // resolved once, here, from ApiEventPayloadMap -- the mirror of the Python build_event map.
            var payload = DeserializePayload(apiEvent);

            LogEvent(apiEvent.Kind);

            switch (apiEvent.Kind)
            {
                case ApiEventKind.AlarmChanged:
                    {
                        if (payload is ApiAlarmStatus ctx)
                        {
                            OnAlarmChanged(ctx);

                            try
                            {
                                await _deviceStore.AddAlarmHistoryAsync(ctx);
                            }
                            catch (Exception ex)
                            {
                                LogPersistAlarmHistoryFailed(ex, ctx.AlarmId);
                            }
                        }
                        break;
                    }
                case ApiEventKind.DetectorChanged:
                    {
                        if (payload is ApiDetectorStatus ctx)
                        {
                            OnDetectorChanged(ctx);

                            try
                            {
                                await _deviceStore.AddDetectorHistoryAsync(ctx);
                            }
                            catch (Exception ex)
                            {
                                LogPersistDetectorHistoryFailed(ex, ctx.DetectorId);
                            }
                        }
                        break;
                    }
                // The two emergency kinds are handled separately on purpose: ApiEmergencyStopPayload does NOT
                // derive from ApiReasonPayload, so a shared `is ApiReasonPayload` test would silently drop
                // every stop. Each insert is also half of the notification rendezvous -- see OnNotificationSent.
                case ApiEventKind.EmergencyStop:
                    {
                        if (payload is ApiEmergencyStopPayload ctx)
                        {
                            var key = (apiEvent.Kind, (long)apiEvent.Index);

                            // Read-and-remove first: if the insert throws, the pending stamp dies with the row
                            // it would have marked, which is correct -- there is nothing to stamp.
                            DateTime? alreadySent = _pendingStamps.Remove(key, out var pending) ? pending.Value : null;

                            try
                            {
                                var id = await _deviceStore.AddEmergencyStopAsync(
                                    ctx, ToUtc(apiEvent.When), key.Item2, alreadySent);

                                if (alreadySent is null)
                                    RememberEmergencyRow(key, id);
                            }
                            catch (Exception ex)
                            {
                                LogPersistEmergencyFailed(ex, apiEvent.Kind);
                            }
                        }
                        break;
                    }
                case ApiEventKind.EmergencyResume:
                    {
                        if (payload is ApiEmergencyResumePayload ctx)
                        {
                            var key = (apiEvent.Kind, (long)apiEvent.Index);

                            DateTime? alreadySent = _pendingStamps.Remove(key, out var pending) ? pending.Value : null;

                            try
                            {
                                var id = await _deviceStore.AddEmergencyResumeAsync(
                                    ctx, ToUtc(apiEvent.When), key.Item2, alreadySent);

                                if (alreadySent is null)
                                    RememberEmergencyRow(key, id);
                            }
                            catch (Exception ex)
                            {
                                LogPersistEmergencyFailed(ex, apiEvent.Kind);
                            }
                        }
                        break;
                    }
                // Tunnel events no longer define a session -- the producer does, and every lifecycle event
                // carries its session_id. They still drive system state and reach the hub.
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
                case ApiEventKind.SessionStarted:
                    {
                        if (payload is ApiSessionStartedPayload ctx)
                            await PersistToAnimalAsync(apiEvent.Kind, id => _animalStore.ApplySessionStartedAsync(
                                id, ctx.SessionId, ToUtc(apiEvent.When), ctx.IsAnalysisDeferred));
                        break;
                    }
                case ApiEventKind.SessionEnded:
                    {
                        if (payload is ApiSessionEndedPayload ctx)
                        {
                            var id = Animal?.Identifier;
                            if (string.IsNullOrWhiteSpace(id))
                            {
                                LogNoAnimalSelected(apiEvent.Kind);
                            }
                            else
                            {
                                try
                                {
                                    var summary = await _animalStore.ApplySessionEndedAsync(id, ctx.SessionId,
                                        ToUtc(apiEvent.When), ctx.CaptureTrialCount, ctx.AnalysisTrialCount,
                                        ctx.FailedTrialCount);
                                    if (summary is not null)
                                    {
                                        // 24h count = the rolling window (the "d" unit), recomputed as of now.
                                        var count = await _animalStore.CountSessionsAsync(id,
                                            DateTime.UtcNow - TimeSpan.FromDays(1), isAnalysisDeferred: null);
                                        await _hubContext.Clients.All.SessionEnded(
                                            new SessionEnded(Animal?.Identifier, summary, count));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogPersistEventFailed(ex, apiEvent.Kind, id);
                                }
                            }
                        }
                        break;
                    }
                case ApiEventKind.BatchAnalysisStarted:
                    {
                        if (payload is ApiBatchAnalysisStartedPayload ctx)
                            await PersistToAnimalAsync(apiEvent.Kind, id => _animalStore.ApplyBatchAnalysisStartedAsync(
                                id, ctx.SessionId, ctx.BatchId, ToUtc(apiEvent.When), ctx.AnalysisTrialCount));
                        break;
                    }
                case ApiEventKind.BatchAnalysisEnded:
                    {
                        if (payload is ApiBatchAnalysisEndedPayload ctx)
                            await PersistToAnimalAsync(apiEvent.Kind, id => _animalStore.ApplyBatchAnalysisEndedAsync(
                                id, ctx.SessionId, ctx.BatchId, ToUtc(apiEvent.When),
                                ctx.AnalysisTrialCount, ctx.FailedTrialCount));
                        break;
                    }
                case ApiEventKind.TrialStarted:
                    {
                        if (payload is ApiTrialStartedPayload ctx)
                            await ApplyTrialEventAsync(apiEvent,
                                new TrialEventValues(ctx.SessionId, ctx.TrialId, null, ToUtc(apiEvent.When))
                                {
                                    Reason = ctx.Reason
                                });
                        break;
                    }
                case ApiEventKind.TrialEnded:
                    {
                        if (payload is ApiTrialEndedPayload ctx)
                            await ApplyTrialEventAsync(apiEvent,
                                new TrialEventValues(ctx.SessionId, ctx.TrialId, null, ToUtc(apiEvent.When))
                                {
                                    Result = ctx.Result
                                });
                        break;
                    }
                case ApiEventKind.TrialCaptureEnded:
                case ApiEventKind.TrialPelletPresented:
                    {
                        if (payload is ApiSessionTrialPayload ctx)
                            await ApplyTrialEventAsync(apiEvent,
                                new TrialEventValues(ctx.SessionId, ctx.TrialId, null, ToUtc(apiEvent.When)));
                        break;
                    }
                case ApiEventKind.TrialAnimalSeen:
                case ApiEventKind.TrialRightHandSeen:
                case ApiEventKind.TrialPelletSeen:
                    {
                        // BatchId is deliberately not read: it is deprecated and meaningless on these events
                        // (they fire during capture, before any batch exists). Reading it would create a
                        // bogus BatchAnalysis row.
                        if (payload is ApiTrialSeenPayload ctx)
                            await ApplyTrialEventAsync(apiEvent,
                                new TrialEventValues(ctx.SessionId, ctx.TrialId, null, ToUtc(apiEvent.When)));
                        break;
                    }
                case ApiEventKind.IntertrialSegmentationBegin:
                case ApiEventKind.IntertrialSegmentationEnd:
                case ApiEventKind.IntertrialDetectionBegin:
                case ApiEventKind.IntertrialDetectionEnd:
                    {
                        if (payload is ApiAnalysisTrialPayload ctx)
                            await ApplyTrialEventAsync(apiEvent,
                                new TrialEventValues(ctx.SessionId, ctx.TrialId, ctx.BatchId, ToUtc(apiEvent.When)));
                        break;
                    }
                case ApiEventKind.IntertrialSegmentationError:
                case ApiEventKind.IntertrialSegmentationSaveError:
                case ApiEventKind.IntertrialDetectionError:
                case ApiEventKind.IntertrialDetectionSaveError:
                    {
                        if (payload is ApiIntertrialErrorPayload ctx)
                            await ApplyTrialEventAsync(apiEvent,
                                new TrialEventValues(ctx.SessionId, ctx.TrialId, ctx.BatchId, ToUtc(apiEvent.When))
                                {
                                    Error = ctx.Error
                                });
                        break;
                    }
                case ApiEventKind.IntertrialSegmentationSave:
                case ApiEventKind.IntertrialDetectionSave:
                    {
                        if (payload is ApiIntertrialSavePayload ctx)
                            await ApplyTrialEventAsync(apiEvent,
                                new TrialEventValues(ctx.SessionId, ctx.TrialId, ctx.BatchId, ToUtc(apiEvent.When))
                                {
                                    Location = ctx.Location
                                });
                        break;
                    }
                case ApiEventKind.IntertrialPelletShift:
                    {
                        if (payload is ApiPelletShiftPayload ctx)
                            await ApplyTrialEventAsync(apiEvent,
                                new TrialEventValues(ctx.SessionId, ctx.TrialId, ctx.BatchId, ToUtc(apiEvent.When))
                                {
                                    PelletShiftJson = JsonSerializer.Serialize(ctx, s_JsonOptions)
                                });
                        break;
                    }
                case ApiEventKind.LoadCellEngagedChanged:
                    {
                        if (payload is ApiIsEngagedPayload ctx)
                        {
                            Analysis.LoadCellEngaged = ctx.IsEngaged;
                            await _hubContext.Clients.All.AnalysisChanged(Analysis);
                        }
                        break;
                    }
                case ApiEventKind.HeadbarPressureEngagedChanged:
                    {
                        if (payload is ApiIsEngagedPayload ctx)
                        {
                            Analysis.HeadbarPressureEngaged = ctx.IsEngaged;
                            await _hubContext.Clients.All.AnalysisChanged(Analysis);
                        }
                        break;
                    }
                case ApiEventKind.HeadfixBaselineChanged:
                    {
                        if (payload is ApiBaselinePayload ctx)
                        {
                            Behavior.BaselineMagnetIntensity = ctx.Baseline;
                            await _hubContext.Clients.All.BehaviorChanged(Behavior);
                        }
                        break;
                    }
                case ApiEventKind.HeadfixLoadCellEnabledChanged:
                    {
                        // This event's payload is IsEnabledContext ("isEnabled"), not IsEngagedContext.
                        if (payload is ApiIsEnabledPayload ctx)
                        {
                            Behavior.LoadCellTriggered = ctx.IsEnabled;
                            await _hubContext.Clients.All.BehaviorChanged(Behavior);
                        }
                        break;
                    }
                case ApiEventKind.AutoClampEnabledChanged:
                    {
                        // Alone among the 1000-1099 behavior settings, auto-clamp publishes an event when it
                        // changes, so the model does not have to wait for the next systemStatus to catch up.
                        if (payload is ApiIsEnabledPayload ctx)
                        {
                            Behavior.IsAutoClampEnabled = ctx.IsEnabled;
                            await _hubContext.Clients.All.BehaviorChanged(Behavior);
                        }
                        break;
                    }
                case ApiEventKind.HeadFixationForceDetectorChanged:
                    {
                        // IsEnabledContext, like HeadfixLoadCellEnabledChanged. Distinct from
                        // HeadbarPressureEngagedChanged, which carries IsEngagedContext and drives Analysis.
                        if (payload is ApiIsEnabledPayload ctx)
                        {
                            Behavior.HeadbarPressureTriggered = ctx.IsEnabled;
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
                        // These do not share one payload type (the Begin events are CommandContext, the resets
                        // carry their own), and nothing here reads it -- only that a payload arrived at all.
                        if (payload != null)
                        {
                            PelletDevice.UpdateLastCommand(apiEvent);
                            await _hubContext.Clients.All.PelletDeviceChanged(PelletDevice);
                        }
                        break;
                    }
                case ApiEventKind.SystemStatus:
                    {
                        if (payload is ApiSystemStatus ctx)
                        {
                            await OnSystemStatusChangedAsync(ctx);

                            await PersistReachStatusAsync(ctx);
                        }
                        break;
                    }
                case ApiEventKind.DayStarted:
                    {
                        // DayStarted does NOT move the 5-day window (that follows the authoritative DayPath only).
                        // It does set the day-attribution clock: its epoch, read in the device's local timezone (the
                        // producer's), tells us a new day has begun so day*CountChanged events land on the new day
                        // even before the next systemStatus. The 5-day total self-corrects on that next systemStatus.
                        if (payload is ApiDayStartedPayload started)
                            _lastDayStartedDay = LocalDateFromEpoch(started.Date);
                        break;
                    }
                case ApiEventKind.PelletPresentedCountChanged:
                case ApiEventKind.PelletConsumedCountChanged:
                case ApiEventKind.ReachCountChanged:
                case ApiEventKind.SuccessfulReachesCountChanged:
                case ApiEventKind.DayPelletPresentedCountChanged:
                case ApiEventKind.DayPelletConsumedCountChanged:
                case ApiEventKind.DayReachCountChanged:
                case ApiEventKind.DaySuccessfulReachesCountChanged:
                    {
                        // Each event is the new absolute Count for one column of the total or day reach-status
                        // table (the Change field is ignored). Same destination tables as a systemStatus snapshot,
                        // one column at a time.
                        if (payload is ApiCountChangePayload ctx)
                        {
                            var (scope, field) = ReachCountTarget(apiEvent.Kind);

                            OnReachCountChanged(scope, field, ctx.Count);

                            var attributionDay = CurrentDeviceDay;
                            await PersistToAnimalAsync(apiEvent.Kind, id =>
                                _animalStore.ApplyReachCountChangeAsync(id, scope, field, ctx.Count, attributionDay));
                        }
                        break;
                    }
                case ApiEventKind.AnimalSelected:
                case ApiEventKind.AnimalUpdated:
                    {
                        if (payload is ApiAnimalStatus status)
                        {
                            if (string.IsNullOrWhiteSpace(status.Identifier))
                            {
                                LogMissingAnimalIdentifier(apiEvent.Kind);
                            }
                            else
                            {
                                // Seed the running 5-day total for the (possibly new) animal before broadcasting,
                                // so the single AnimalChanged below already carries ReachStatus5Day.
                                await RefreshFiveDayAsync(status.Identifier, day: null, currentDayCounts: null);
                                await RefreshAnimalNotesAsync(status.Identifier);

                                // The selection drives which animal database every lifecycle event is written
                                // to, so apply it here rather than waiting for the next systemStatus.
                                OnAnimalChanged(status);

                                try
                                {
                                    await _animalStore.AddAnimalHistoryAsync(status);
                                }
                                catch (Exception ex)
                                {
                                    LogPersistAnimalFailed(ex, status.Identifier);
                                }
                            }
                        }
                        else if (apiEvent.Kind == ApiEventKind.AnimalSelected)
                        {
                            // animalSelected carries an optional payload; no payload means the selection was
                            // cleared. Clear it here too, or later events would keep writing to this animal.
                            OnAnimalChanged(null);
                        }
                        break;
                    }
                case ApiEventKind.TrialReachEvents:
                    {
                        if (payload is ApiTrialReachEventsPayload ctx)
                        {
                            OnReachEventsChanged(ctx.TrialReachEvents);

                            // No Count > 0 guard: an empty list must still replace the trial's existing rows.
                            await PersistToAnimalAsync(apiEvent.Kind, id => _animalStore.ReplaceTrialReachEventsAsync(
                                id, ctx.SessionId, ctx.TrialId, ctx.BatchId, ctx.TrialReachEvents));
                        }
                        break;
                    }
            }

            await _hubContext.Clients.All.EventReceived(apiEvent);
        });
    }

    // Records that an operator notification went out for this event. Queued on the same channel as the history
    // insert so the two are serialized, but NOT assumed to run after it -- the emergency-topic copy and the
    // event-topic copy are handled by independent workers, so if the row does not exist yet the send time is
    // held for the insert to apply.
    public void OnNotificationSent(ApiEvent apiEvent, DateTime sentAt)
    {
        _updateChannel.Writer.TryWrite(async () =>
        {
            var key = (apiEvent.Kind, (long)apiEvent.Index);

            try
            {
                if (_emergencyRowIds.Remove(key, out var row))
                    await _deviceStore.MarkNotificationSentAsync(row.Value, sentAt);
                else
                    RememberPendingStamp(key, sentAt);
            }
            catch (Exception ex)
            {
                LogStampNotificationFailed(ex, apiEvent.Kind);
            }
        });
    }

    private void RememberEmergencyRow((ApiEventKind Kind, long Index) key, int id)
    {
        EvictCorrelations(_emergencyRowIds, nameof(_emergencyRowIds));
        _emergencyRowIds[key] = (id, DateTime.UtcNow);
    }

    private void RememberPendingStamp((ApiEventKind Kind, long Index) key, DateTime sentAt)
    {
        EvictCorrelations(_pendingStamps, nameof(_pendingStamps));
        _pendingStamps[key] = (sentAt, DateTime.UtcNow);
    }

    // Drops correlation entries that have provably lost their counterpart, and only those.
    //
    // Age is the only safe criterion. Evicting by count -- clearing the map, or dropping the oldest entry to
    // hold a hard cap -- can discard the id of an event whose SNS publish is still running: the stamp then
    // arrives, finds nothing to update, and parks a pending entry that its already-completed insert can never
    // consume, leaving a delivered notification permanently recorded as unsent. A burst of emergency events
    // during one slow publish is exactly when that happens, so a recent entry is never evicted.
    //
    // The map is therefore bounded by the timeout rather than by a count: at any plausible emergency rate
    // that is a handful of entries, and each is two longs and a timestamp.
    private void EvictCorrelations<TValue>(
        Dictionary<(ApiEventKind Kind, long Index), (TValue Value, DateTime AddedAt)> map, string name)
    {
        // The threshold only decides when it is worth scanning; it is not a cap.
        if (map.Count < EmergencyCorrelationSweepThreshold)
            return;

        var cutoff = DateTime.UtcNow - EmergencyCorrelationTimeout;

        var expired = map.Where(e => e.Value.AddedAt < cutoff).Select(e => e.Key).ToList();

        foreach (var key in expired)
        {
            map.Remove(key);
            LogEmergencyCorrelationEvicted(name, key.Kind, key.Index);
        }

        // Still large after sweeping means many counterparts are genuinely in flight at once. Surface it
        // rather than dropping data to force the number down.
        if (map.Count >= EmergencyCorrelationSweepThreshold)
            LogEmergencyCorrelationsRetained(name, map.Count);
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
                            await OnSystemConfigurationChangedAsync(config);

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
                            await OnSystemStatusChangedAsync(status);
                        }
                        break;
                    }
                default:
                    {
                        // A behavior setting's response is the only thing that converges the retained model for
                        // nine of the ten: auto-clamp alone also publishes an event, and nothing schedules a
                        // systemStatus after a setter, so without this the model stays stale until an unrelated
                        // snapshot happens to arrive.
                        if (!IsBehaviorSetting(response.Command))
                            break;

                        if (DeserializeData<ApiEnabledPayload>(response.Data) is not { } payload)
                            break;

                        // The response carries the state actually in effect, which is what makes applying it
                        // directly correct rather than a guess at what the request asked for.
                        switch (ApplyBehaviorSetting(response.Command, payload.Enabled))
                        {
                            case SettingModel.Behavior:
                                await _hubContext.Clients.All.BehaviorChanged(Behavior);
                                break;
                            case SettingModel.PelletDevice:
                                await _hubContext.Clients.All.PelletDeviceChanged(PelletDevice);
                                break;
                        }
                        break;
                    }
            }
        });
    }

    // Which retained model a behavior setting lands on, and therefore which broadcast carries it.
    private enum SettingModel { Behavior, PelletDevice }

    // The behavior settings occupy 1000-1099 by contract (see ApiCommandKind), which is what makes a range
    // test safe here and keeps the kind-to-property mapping in exactly one place below.
    private static bool IsBehaviorSetting(ApiCommandKind command) => (int)command is >= 1000 and < 1100;

    // Applies a successful setting response to the retained model and reports which model changed.
    //
    // One switch rather than a lookup table plus a separate apply: a second table over the same ten kinds is
    // precisely where a setting gets wired to the wrong property, and nothing downstream would notice.
    private SettingModel? ApplyBehaviorSetting(ApiCommandKind command, bool enabled)
    {
        switch (command)
        {
            case ApiCommandKind.SetLiveAnalysisEnabled:
                Behavior.IsLiveAnalysisEnabled = enabled;
                return SettingModel.Behavior;
            case ApiCommandKind.SetPelletDeliveryEnabled:
                Behavior.IsPelletDeliveryEnabled = enabled;
                return SettingModel.Behavior;
            case ApiCommandKind.SetPelletCoverEnabled:
                Behavior.IsPelletCoverEnabled = enabled;
                return SettingModel.Behavior;
            case ApiCommandKind.SetIntertrialPelletShiftEnabled:
                Behavior.IsIntertrialPelletShiftEnabled = enabled;
                return SettingModel.Behavior;
            case ApiCommandKind.SetTrianglePelletDistanceDetectionEnabled:
                Behavior.IsTrianglePelletDistanceDetectionEnabled = enabled;
                return SettingModel.Behavior;
            case ApiCommandKind.SetAutoCloseGateOnIntertrialEnabled:
                Behavior.IsAutoCloseGateOnIntertrialEnabled = enabled;
                return SettingModel.Behavior;
            case ApiCommandKind.SetAutoClampEnabled:
                Behavior.IsAutoClampEnabled = enabled;
                return SettingModel.Behavior;
            case ApiCommandKind.SetBatchTrialsEnabled:
                Behavior.IsBatchTrialsEnabled = enabled;
                return SettingModel.Behavior;

            // Reported on the pellet device rather than on ApiBehaviorStatus, so these two broadcast differently.
            case ApiCommandKind.SetHomeOnExcessiveDriftEnabled:
                PelletDevice.IsHomeOnExcessiveDriftEnabled = enabled;
                return SettingModel.PelletDevice;
            case ApiCommandKind.SetTunnelSweepEnabled:
                PelletDevice.IsTunnelSweepEnabled = enabled;
                return SettingModel.PelletDevice;

            // In the 1000-1099 range but not a setting this build knows: a newer producer added it.
            default:
                LogUnmappedBehaviorSetting(command);
                return null;
        }
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

    // Resolves the payload type from the kind (the event's only discriminator) and deserializes the context
    // into it. Returns null when the kind has no payload, the event carried none, or the payload is malformed
    // -- a bad payload must not take down the whole event (the broadcast still has to happen).
    private object? DeserializePayload(ApiEvent apiEvent)
    {
        if (apiEvent.Context is not JsonElement element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var type = ApiEventPayloadMap.PayloadType(apiEvent.Kind);

        if (type is null)
            return null;

        try
        {
            return element.Deserialize(type, s_JsonOptions);
        }
        catch (JsonException ex)
        {
            LogPayloadDeserializeFailed(ex, apiEvent.Kind, type.Name);
            return null;
        }
    }

    private Task ApplyTrialEventAsync(ApiEvent apiEvent, TrialEventValues values) =>
        PersistToAnimalAsync(apiEvent.Kind, id => _animalStore.ApplyTrialEventAsync(id, apiEvent.Kind, values));

    // Maps each *CountChanged event to the reach-status table (Total vs Day) and column it updates. The Day
    // events are the same four columns in the day table.
    private static (ReachCountScope Scope, ReachCountField Field) ReachCountTarget(ApiEventKind kind) => kind switch
    {
        ApiEventKind.PelletPresentedCountChanged => (ReachCountScope.Total, ReachCountField.PelletsPresented),
        ApiEventKind.PelletConsumedCountChanged => (ReachCountScope.Total, ReachCountField.PelletsConsumed),
        ApiEventKind.ReachCountChanged => (ReachCountScope.Total, ReachCountField.Reaches),
        ApiEventKind.SuccessfulReachesCountChanged => (ReachCountScope.Total, ReachCountField.SuccessfulReaches),
        ApiEventKind.DayPelletPresentedCountChanged => (ReachCountScope.Day, ReachCountField.PelletsPresented),
        ApiEventKind.DayPelletConsumedCountChanged => (ReachCountScope.Day, ReachCountField.PelletsConsumed),
        ApiEventKind.DayReachCountChanged => (ReachCountScope.Day, ReachCountField.Reaches),
        ApiEventKind.DaySuccessfulReachesCountChanged => (ReachCountScope.Day, ReachCountField.SuccessfulReaches),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a reach-count-change event kind.")
    };

    // Every animal-database write goes through here. If no animal is selected the event is simply ignored:
    // sessions can run with no animal involved, so this is expected and is not an error condition.
    private async Task PersistToAnimalAsync(ApiEventKind kind, Func<string, Task> write)
    {
        var identifier = Animal?.Identifier;

        if (string.IsNullOrWhiteSpace(identifier))
        {
            LogNoAnimalSelected(kind);
            return;
        }

        try
        {
            await write(identifier);
        }
        catch (Exception ex)
        {
            LogPersistEventFailed(ex, kind, identifier);
        }
    }

    // Uses the status's own animal rather than the Animal property: OnSystemStatusChanged has just set the
    // selection from this very message, so the payload is the unambiguous source.
    private async Task PersistReachStatusAsync(ApiSystemStatus status)
    {
        if (status.Animal is not { } animal || string.IsNullOrWhiteSpace(animal.Identifier))
            return;

        try
        {
            await _animalStore.AddReachStatusIfChangedAsync(
                animal.Identifier, animal.ReachStatusTotal, animal.ReachStatusDay,
                DayPath.TryGetDay(status.Project.DayPath));
        }
        catch (Exception ex)
        {
            LogPersistReachStatusFailed(ex, animal.Identifier);
        }
    }

    // ApiEvent.When is a producer epoch-seconds timestamp; fall back to server time if it is unset.
    private static DateTime ToUtc(double epochSeconds) =>
        epochSeconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(epochSeconds * 1000)).UtcDateTime
            : DateTime.UtcNow;

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
            _fiveDay = default;
            _fiveDayId = null;
            _animalNotes = AnimalNotes.Empty;
            _animalNotesId = null;
        }

        StampAndBroadcastAnimal();
    }

    // Stamps the values that are computed or stored server-side rather than carried by any message -- the running
    // 5-day total and the registry notes -- onto the live Animal and broadcasts it. Every animal broadcast goes
    // through here, so both are always fresh.
    private void StampAndBroadcastAnimal()
    {
        if (Animal is { } animal)
        {
            animal.ReachStatus5Day.ApplyStatus(_fiveDay.Total);
            animal.TrainerNotes = _animalNotes.TrainerNotes;
            animal.BehaviorNote = _animalNotes.BehaviorNote;
        }

        _hubContext.Clients.All.AnimalChanged(Animal);
    }

    // Applies a single-column *CountChanged event to the live Animal, keeping it current between the periodic
    // systemStatus snapshots that otherwise refresh it. No-ops when no animal is selected (nothing to update),
    // mirroring the database write.
    private void OnReachCountChanged(ReachCountScope scope, ReachCountField field, int count)
    {
        if (Animal is not { } animal)
            return;

        var target = scope == ReachCountScope.Total ? animal.ReachStatusTotal : animal.ReachStatusDay;
        switch (field)
        {
            case ReachCountField.PelletsPresented: target.PelletsPresented = count; break;
            case ReachCountField.PelletsConsumed: target.PelletsConsumed = count; break;
            case ReachCountField.Reaches: target.Reaches = count; break;
            case ReachCountField.SuccessfulReaches: target.SuccessfulReaches = count; break;
        }

        // A day-scope change moves today's contribution to the running 5-day total (in memory — no rollover, so
        // no database read). Patch the one column on the DB-seeded current-day counts (not on animal.ReachStatusDay,
        // whose other columns are only current as of the last systemStatus). Total-scope changes don't affect it.
        if (scope == ReachCountScope.Day)
            _fiveDay = _fiveDay with { CurrentDayCounts = WithCount(_fiveDay.CurrentDayCounts, field, count) };

        StampAndBroadcastAnimal();
    }

    // Brings _fiveDay up to date for `identifier`. A database read (reseed of the earlier-four-days base) happens
    // only when the animal changes or the DayPath day rolls over; otherwise the current day's counts are just
    // replaced in memory. `day` is the authoritative DayPath day (null when unknown, e.g. a count event or a bare
    // re-evaluation); `currentDayCounts`, when given, is the day table's latest counts to apply.
    private async Task RefreshFiveDayAsync(string? identifier, DateOnly? day, ApiReachStatus? currentDayCounts)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            _fiveDay = default;
            _fiveDayId = null;
            return;
        }

        if (identifier != _fiveDayId || (day is { } d && d != _fiveDay.CurrentDay))
        {
            try
            {
                _fiveDay = await _animalStore.LoadFiveDayReachStatusAsync(identifier, day);
            }
            catch (Exception ex)
            {
                // Reset to an empty window (not the previous animal's sums) so a failed seed can't leak another
                // animal's totals; the next systemStatus will reseed.
                LogFiveDayLoadFailed(ex, identifier);
                _fiveDay = new FiveDayReachStatus(day, default, default);
            }

            _fiveDayId = identifier;
        }

        if (currentDayCounts is { } counts)
            _fiveDay = _fiveDay with { CurrentDay = _fiveDay.CurrentDay ?? day, CurrentDayCounts = counts };
    }

    // Brings _animalNotes up to date for `identifier`. One database read per animal; `force` re-reads the animal
    // already cached, which is what a note mutation with no selection change needs. Returns false when the read
    // threw, so a caller that only re-broadcasts on a real change can tell a failure apart from a note that
    // legitimately became null.
    private async Task<bool> RefreshAnimalNotesAsync(string? identifier, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            _animalNotes = AnimalNotes.Empty;
            _animalNotesId = null;
            return true;
        }

        if (!force && identifier == _animalNotesId)
            return true;

        try
        {
            _animalNotes = await _deviceStore.GetAnimalNotesAsync(identifier);
        }
        catch (Exception ex)
        {
            LogAnimalNotesLoadFailed(ex, identifier);

            // A selection change must not leave the previous animal's notes on screen -- misattributed free text
            // is worse than none. A forced re-read of the animal already cached is the *same* animal, so there is
            // nothing to misattribute and the retained notes stay put; blanking them here would instead publish a
            // spurious "the note vanished" and poison the cache, since _animalNotesId still matches and no later
            // non-forced refresh would re-read it.
            if (identifier != _animalNotesId)
                _animalNotes = AnimalNotes.Empty;

            _animalNotesId = identifier;
            return false;
        }

        _animalNotesId = identifier;
        return true;
    }

    // Sets a single column, mirroring the store's per-column carry-forward so the in-memory 5-day current-day
    // counts stay identical to the row the store appends for the same event.
    private static ApiReachStatus WithCount(ApiReachStatus s, ReachCountField field, int count) => field switch
    {
        ReachCountField.PelletsPresented => s with { PelletsPresented = count },
        ReachCountField.PelletsConsumed => s with { PelletsConsumed = count },
        ReachCountField.Reaches => s with { Reaches = count },
        ReachCountField.SuccessfulReaches => s with { SuccessfulReaches = count },
        _ => s
    };

    // DayStarted.Date is epoch seconds for the producer's local midnight; the device runs in the producer's
    // timezone, so the local-time calendar date matches the DayPath date (which the UTC date can miss by a day).
    private static DateOnly LocalDateFromEpoch(double epochSeconds) =>
        DateOnly.FromDateTime(
            DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(epochSeconds * 1000)).LocalDateTime);

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
        AlarmId = s.AlarmId,
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

    private async Task OnSystemConfigurationChangedAsync(ApiSystemConfiguration config)
    {
        Configuration.ApplyStatus(config);

        LogDataLocationUpdated(Configuration.DataLocation);

        try
        {
            // The retained model, not the api object: SystemNote is service-owned and is not on the api type.
            await _hubContext.Clients.All.SystemConfigurationChanged(Configuration);
        }
        catch (Exception ex)
        {
            // Publishing is best-effort; persisting the history row and re-requesting status are not. Awaiting
            // this send (it was fire-and-forget before the payload became the retained model) would otherwise
            // let a faulted broadcast skip both of them in the caller.
            LogBroadcastConfigurationFailed(ex);
        }
    }

    private async Task OnSystemStatusChangedAsync(ApiSystemStatus status)
    {
        OnApplicationModeChanged(status.ApplicationMode);
        OnTrainingModeChanged(status.TrainingMode);

        // DayPath is the authoritative day for both the 5-day window and (as one input) day attribution.
        var dayPathDay = DayPath.TryGetDay(status.Project.DayPath);
        if (dayPathDay is not null)
            _lastDayPathDay = dayPathDay;

        // Refresh the running 5-day total before the animal is broadcast (in OnAnimalChanged) so ReachStatus5Day
        // rides along fresh. A change to the DayPath day rolls the window here.
        await RefreshFiveDayAsync(status.Animal?.Identifier, dayPathDay,
            status.Animal is { } a ? a.ReachStatusDay : null);
        await RefreshAnimalNotesAsync(status.Animal?.Identifier);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize the {kind} payload as {payloadType}; event handled with no payload.")]
    private partial void LogPayloadDeserializeFailed(Exception ex, ApiEventKind kind, string payloadType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{kind} received with no animal identifier; skipping database select/create.")]
    private partial void LogMissingAnimalIdentifier(ApiEventKind kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist animal info for {identifier}")]
    private partial void LogPersistAnimalFailed(Exception ex, string identifier);

    // Not a warning: an event arriving with no animal selected is normal and is simply not stored.
    [LoggerMessage(Level = LogLevel.Debug, Message = "{kind} received with no animal selected; not stored.")]
    private partial void LogNoAnimalSelected(ApiEventKind kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist {kind} for {identifier}")]
    private partial void LogPersistEventFailed(Exception ex, ApiEventKind kind, string identifier);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist reach status for {identifier}")]
    private partial void LogPersistReachStatusFailed(Exception ex, string identifier);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load 5-day reach status for {identifier}")]
    private partial void LogFiveDayLoadFailed(Exception ex, string identifier);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load stored system notes")]
    private partial void LogSystemNotesLoadFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load notes for {identifier}")]
    private partial void LogAnimalNotesLoadFailed(Exception ex, string identifier);

    [LoggerMessage(Level = LogLevel.Information, Message = "Command response {command} {result}")]
    private partial void LogCommandResponse(ApiCommandKind command, ApiCommandRequestResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Behavior setting {command} has no retained model property; the next systemStatus will carry it.")]
    private partial void LogUnmappedBehaviorSetting(ApiCommandKind command);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist system configuration")]
    private partial void LogPersistConfigurationFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to broadcast the system configuration")]
    private partial void LogBroadcastConfigurationFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to broadcast the note change")]
    private partial void LogBroadcastNoteChangeFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist alarm history for {alarmId}")]
    private partial void LogPersistAlarmHistoryFailed(Exception ex, ApiAlarmKind alarmId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist detector history for {detectorId}")]
    private partial void LogPersistDetectorHistoryFailed(Exception ex, ApiDetectorKind detectorId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist emergency history for {kind}")]
    private partial void LogPersistEmergencyFailed(Exception ex, ApiEventKind kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to record the notification stamp for {kind}")]
    private partial void LogStampNotificationFailed(Exception ex, ApiEventKind kind);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Evicted {kind} index {index} from {map}; its counterpart never arrived.")]
    private partial void LogEmergencyCorrelationEvicted(string map, ApiEventKind kind, long index);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "{map} still holds {count} unexpired entries; counterparts are outstanding, not leaked.")]
    private partial void LogEmergencyCorrelationsRetained(string map, int count);

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
