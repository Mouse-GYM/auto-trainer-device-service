namespace AutoTrainer.Api.ApiTypes;

// Mirrors the *Context TypedDicts in autotrainer/api/event/api_event_payloads.py.
// The kind -> payload map is the build_event overload set in autotrainer/api/event/build_event.py.
//
// Events with no payload (AlgorithmPause, AlgorithmResume, TunnelEnter, TunnelExit, HeadfixAutoTare,
// CalibrationDcsCompleted, Calibration3dCompleted, Unknown) have no class here.
// SystemStatus, DetectorChanged, AlarmChanged, and Animal* use the status types in ApiSystemStatus.cs.

// ──────────────────────────────────────────────
// Shared payload types (used by multiple events)
// ──────────────────────────────────────────────

// ReasonContext
// Used by: EmergencyResume, ApplicationTerminating, CalibrationDcsFailed, Calibration3dFailed
public class ApiReasonPayload
{
    public string Reason { get; set; } = "";
}

// SessionTrialContext — identifies a trial within a session; for trial-scoped events with no other data.
// Used by: TrialCaptureEnded, TrialPelletPresented
public class ApiSessionTrialPayload
{
    public string SessionId { get; set; } = "";
    public int TrialId { get; set; }
}

// AnalysisTrialContext — a trial-scoped analysis-phase event, plus the batch that produced it.
// BatchId is null when there is no batch (inline analysis, or analysis disabled).
// Used by: IntertrialSegmentationBegin, IntertrialSegmentationEnd,
//          IntertrialDetectionBegin, IntertrialDetectionEnd
public class ApiAnalysisTrialPayload
{
    public string SessionId { get; set; } = "";
    public int TrialId { get; set; }
    public string? BatchId { get; set; }
}

// TrialSeenContext — a first-sighting event.
// DEPRECATED FIELD: BatchId on these events is meaningless and must not be read. They are emitted during
// capture, before any batch exists. The key is retained only for producer compatibility.
// Used by: TrialAnimalSeen, TrialRightHandSeen, TrialPelletSeen
public class ApiTrialSeenPayload
{
    public string SessionId { get; set; } = "";
    public int TrialId { get; set; }
    public string? BatchId { get; set; }   // DEPRECATED / ignored — do not read.
}

// CommandContext — correlates a hardware command with its acknowledgement.
// Used by: PelletLoadBegin, PelletLoadEnd, PelletSendBegin, PelletSendEnd, PelletCoverBegin, PelletCoverEnd,
//          PelletReleaseBegin, PelletReleaseEnd, PelletHomeBegin, PelletHomeEnd, PelletRetractBegin,
//          PelletRetractEnd, TunnelGateOpenBegin, TunnelGateOpenEnd, TunnelGateCloseBegin,
//          TunnelGateCloseEnd, TunnelFanOnBegin, TunnelFanOnEnd, TunnelFanOffBegin, TunnelFanOffEnd,
//          DeviceCommandSend, DeviceCommandAcknowledge
public class ApiCommandPayload
{
    public string Context { get; set; } = "";
}

// IsEnabledContext
// Used by: HeadfixLoadCellEnabledChanged, HeadfixLoadCellChangedInIntersession,
//          HeadfixLoadCellChangedWrongState, HeadFixationForceDetectorChanged, AutoClampEnabledChanged
public class ApiIsEnabledPayload
{
    public bool IsEnabled { get; set; }
}

// IsEngagedContext
// Used by: LoadCellEngagedChanged, HeadbarPressureEngagedChanged
public class ApiIsEngagedPayload
{
    public bool IsEngaged { get; set; }
}

// IntertrialErrorContext. Sending this is assumed to also indicate the corresponding non-error event.
// Used by: IntertrialSegmentationError, IntertrialSegmentationSaveError,
//          IntertrialDetectionError, IntertrialDetectionSaveError
public class ApiIntertrialErrorPayload
{
    public string SessionId { get; set; } = "";
    public int TrialId { get; set; }
    public string? BatchId { get; set; }
    public string Error { get; set; } = "";
}

// IntertrialSaveContext
// Used by: IntertrialSegmentationSave, IntertrialDetectionSave
public class ApiIntertrialSavePayload
{
    public string SessionId { get; set; } = "";
    public int TrialId { get; set; }
    public string? BatchId { get; set; }
    public string Location { get; set; } = "";
}

// CountChangeContext — a running counter update.
// Used by: PelletPresentedCountChanged, PelletConsumedCountChanged, ReachCountChanged,
//          SuccessfulReachesCountChanged, DayPelletPresentedCountChanged, DayPelletConsumedCountChanged,
//          DayReachCountChanged, DaySuccessfulReachesCountChanged
public class ApiCountChangePayload
{
    public int Change { get; set; }
    public int Count { get; set; }
}

// IntensityContext
// Used by: AutoClampIntensityChanged, AutoClampEngaged, AutoClampPreDisengage, AutoClampDisengaged
public class ApiIntensityPayload
{
    public double Intensity { get; set; }
}

// TrainingPhaseContext
// Used by: TrainingPhaseEnter, TrainingPhaseExit, TrainingProgressUpdate
public class ApiTrainingPhasePayload
{
    public string TrainingPhaseId { get; set; } = "";
}

// ──────────────────────────────────────────────
// Core/System (0000-0999)
// ──────────────────────────────────────────────

// EmergencyStopContext
//
// ReasonCode is nullable even though the Python TypedDict requires it: producers predating the field are
// still in service and send only `reason`. Null means "this producer did not classify the stop" -- fall
// back to interpreting the Reason string. Do not default it to Unknown, which the producer uses to mean
// "classified, but not a known member" and is a different statement.
public class ApiEmergencyStopPayload
{
    public string Reason { get; set; } = "";
    public ApiEmergencyStopReason? ReasonCode { get; set; }
    public List<ApiAlarmKind> ActiveAlarms { get; set; } = [];
}

// EmergencyResumeContext — extends ReasonContext in Python, so it does here too; a resume payload still
// satisfies `is ApiReasonPayload`.
//
// ResumedAlarms is the alarms that auto-resumed (cleared themselves, and by clearing lifted the stop). It
// is not "the alarms this resume applies to": an alarm still engaged, or one a person resumed past, never
// appears. Only AlarmMonitorResume is expected to be non-empty; every other reason sends [].
//
// See ApiEmergencyStopPayload for why ReasonCode is nullable.
public class ApiEmergencyResumePayload : ApiReasonPayload
{
    public ApiEmergencyResumeReason? ReasonCode { get; set; }
    public List<ApiAlarmKind> ResumedAlarms { get; set; } = [];
}

// VersionContext
// Used by: ApplicationLaunched
public class ApiVersionPayload
{
    public string Version { get; set; } = "";
}

// ApplicationModeContext
// Used by: ApplicationModeChanged
public class ApiApplicationModePayload
{
    public ApiApplicationMode Mode { get; set; }
}

// PropertyChangedContext
public class ApiPropertyChangedPayload
{
    public int Target { get; set; }
    public string Name { get; set; } = "";
    public JsonElement? NewValue { get; set; }
    public JsonElement? OldValue { get; set; }
}

// ProjectChangedContext
public class ApiProjectChangedPayload
{
    public string Root { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Day { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int TrialId { get; set; }
}

// ProjectTrialChangedContext
public class ApiProjectTrialChangedPayload
{
    public string Root { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int TrialId { get; set; }
}

// ──────────────────────────────────────────────
// Behavior (1000-1999)
// ──────────────────────────────────────────────

// PelletHomeResetContext
public class ApiPelletHomeResetPayload
{
    public int Cycles { get; set; }
}

// PelletDriftResetContext
public class ApiPelletDriftResetPayload
{
    public ApiVector3 Drift { get; set; }
}

// SessionStartedContext
public class ApiSessionStartedPayload
{
    public string SessionId { get; set; } = "";
    public bool IsAnalysisDeferred { get; set; }
}

// SessionEndedContext
public class ApiSessionEndedPayload
{
    public string SessionId { get; set; } = "";
    public int CaptureTrialCount { get; set; }
    public int AnalysisTrialCount { get; set; }
    public int FailedTrialCount { get; set; }
}

// TrialStartedContext
public class ApiTrialStartedPayload
{
    public string SessionId { get; set; } = "";
    public int TrialId { get; set; }
    public string Reason { get; set; } = "";
}

// TrialEndedContext. Result is a CaptureAnalysisResult value: capture_only, analysis_succeeded,
// analysis_failed, analysis_delayed.
public class ApiTrialEndedPayload
{
    public string SessionId { get; set; } = "";
    public int TrialId { get; set; }
    public string Result { get; set; } = "";
}

// BatchAnalysisStartedContext
public class ApiBatchAnalysisStartedPayload
{
    public string SessionId { get; set; } = "";
    public string BatchId { get; set; } = "";
    public int AnalysisTrialCount { get; set; }
}

// BatchAnalysisEndedContext
public class ApiBatchAnalysisEndedPayload
{
    public string SessionId { get; set; } = "";
    public string BatchId { get; set; } = "";
    public int AnalysisTrialCount { get; set; }
    public int FailedTrialCount { get; set; }
}

// BaselineContext — the "make baseline" value, not a hardware command.
// Used by: HeadfixBaselineChanged
public class ApiBaselinePayload
{
    public double Baseline { get; set; }
}

// FrequencyContext
// Used by: AutoClampReleaseToneFreqChanged
public class ApiFrequencyPayload
{
    public double Frequency { get; set; }
}

// DelayContext
// Used by: AutoClampReleaseDelayChanged
public class ApiDelayPayload
{
    public double Delay { get; set; }
}

// ReleaseToneContext
// Used by: AutoClampPlayReleaseTone
public class ApiReleaseTonePayload
{
    public double Frequency { get; set; }
    public double Duration { get; set; }
}

// DayStartedContext — seconds since the epoch.
// DayPath is NotRequired in Python: omitted when not known, and absent from older production events.
public class ApiDayStartedPayload
{
    public double Date { get; set; }
    public string? DayPath { get; set; }
}

// IntertrialResponseContext — the trial's intertrial analysis responses.
public class ApiIntertrialResponsePayload
{
    public string SessionId { get; set; } = "";
    public int TrialId { get; set; }
    public string? BatchId { get; set; }
    public IntertrialResponse? ResponseData { get; set; }
}

// TrialReachEventsContext
public class ApiTrialReachEventsPayload
{
    public string SessionId { get; set; } = "";
    public int TrialId { get; set; }
    public string? BatchId { get; set; }
    public List<ReachEvent> TrialReachEvents { get; set; } = [];
}

// PelletShiftContext — sent for any calculated pellet shift, even if not applied.
// Used by: IntertrialPelletShift
public class ApiPelletShiftPayload
{
    public string SessionId { get; set; } = "";
    public int TrialId { get; set; }
    public string? BatchId { get; set; }
    public ApiPelletShiftSource Source { get; set; }
    public ApiVector3 Shift { get; set; }
    public bool Deferred { get; set; }
}

// ──────────────────────────────────────────────
// Training (5000-5999)
// ──────────────────────────────────────────────

// TrainingModeContext
// Used by: TrainingModeChanged
public class ApiTrainingModePayload
{
    public ApiTrainingMode TrainingMode { get; set; }
}

// TrainingPlanContext
// Used by: TrainingPlanLoad
public class ApiTrainingPlanPayload
{
    public string TrainingPlanId { get; set; } = "";
}

// ──────────────────────────────────────────────
// Training — internal behavior (5800-5899)
// ──────────────────────────────────────────────

// Used by: ProtocolEvent, ProtocolPlanEvent, ProtocolPhaseEvent,
//          ProtocolPredicateEvent, ProtocolActionEvent, ProtocolProgressEvent
// Wire format from autotrainer.training.event.api_event.ProtocolEventPayload.to_context()
public class ApiProtocolEventPayload
{
    public string TargetId { get; set; } = "";
    public int EventType { get; set; }
    public IDictionary<string, object>? Data { get; set; }
}

// ──────────────────────────────────────────────
// Detectors & Alarms (6000-6999)
// ──────────────────────────────────────────────
// DetectorChanged uses Payload <ApiDetectorStatus> directly (defined in ApiSystemStatus.cs).
// AlarmChanged uses Payload <ApiAlarmStatus> directly (defined in ApiSystemStatus.cs).

// ──────────────────────────────────────────────
// Helper types
// ──────────────────────────────────────────────

// From autotrainer.core — not enums by design; values are persisted as strings in h5 files.
public static class ReachEventMethod
{
    public const string None = "none";
    public const string Other = "other";
    public const string RightHand = "right_hand";
    public const string LeftHand = "left_hand";
    public const string Tongue = "tongue";

    // Stable integer codes for persistence (ReachEvent.Method).
    public static int ToCode(string method) => method switch
    {
        Other => 1,
        RightHand => 2,
        LeftHand => 3,
        Tongue => 4,
        _ => 0
    };
}

public static class ReachEventOutcome
{
    public const string None = "none";
    public const string Stalled = "stalled";
    public const string Missed = "missed";
    public const string Dropped = "dropped";
    public const string Grabbed = "grabbed";
    public const string Eaten = "eaten";

    // Stable integer codes for persistence (ReachEvent.Outcome).
    public static int ToCode(string outcome) => outcome switch
    {
        Stalled => 1,
        Missed => 2,
        Dropped => 3,
        Grabbed => 4,
        Eaten => 5,
        _ => 0
    };
}

// IntertrialResponseDict — from autotrainer.inference.analysis.IntertrialResponse.
// RhMaxVpList entries are (x, y, z) relative offsets, each null when the offset could not be determined.
// The source type also carries a deprecated pellets_presented that rides over the wire and is not mirrored.
public class IntertrialResponse
{
    public List<List<double>?> RhMaxVpList { get; set; } = [];
    public List<ReachEvent> ReachEvents { get; set; } = [];
    public List<ReachEvent> OtherEvents { get; set; } = [];
    public int FoodConsumed { get; set; }
    public int SuccessfulReaches { get; set; }
    public int TotalReaches { get; set; }
}

// ReachEventDict — from autotrainer.core.ReachEvent
public class ReachEvent
{
    public int Init { get; set; }
    public int? End { get; set; }
    public int? Max { get; set; }
    public string Method { get; set; } = ReachEventMethod.None;
    public string Outcome { get; set; } = ReachEventOutcome.None;
    public double DelaySincePresented { get; set; }
}

// Vector3Context
public struct ApiVector3
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}
