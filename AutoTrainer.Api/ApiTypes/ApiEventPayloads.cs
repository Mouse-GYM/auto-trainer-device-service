namespace AutoTrainer.Api.ApiTypes;

// ──────────────────────────────────────────────
// Shared payload types (used by multiple events)
// ──────────────────────────────────────────────

// Used by: EmergencyResume, ApplicationTerminating, CalibrationDcsFailed, Calibration3dFailed, TrialCaptureEnded
public class ApiReasonPayload
{
    public string Reason { get; set; } = "";
}

// Used by: PelletLoadBegin, PelletLoadEnd, PelletSendBegin, PelletSendEnd, PelletCoverBegin, PelletCoverEnd,
//          PelletReleaseBegin, PelletReleaseEnd, PelletHomeBegin, PelletHomeEnd, PelletRetractBegin, PelletRetractEnd,
//          TunnelGateOpenBegin, TunnelGateOpenEnd, TunnelGateCloseBegin, TunnelGateCloseEnd,
//          TunnelFanOnBegin, TunnelFanOnEnd, TunnelFanOffBegin, TunnelFanOffEnd,
//          DeviceCommandSend, DeviceCommandAcknowledge
public class ApiContextPayload
{
    public string Context { get; set; } = "";
}

// Used by: HeadfixLoadCellEnabledChanged, HeadfixLoadCellChangedInIntersession, HeadfixLoadCellChangedWrongState,
//          HeadFixationForceDetectorChanged, AutoClampEnabledChanged
public class ApiEnabledChangedPayload
{
    public bool IsEnabled { get; set; }
}

// Used by: LoadCellEngagedChanged, HeadbarPressureEngagedChanged
public class ApiEngagedChangedPayload
{
    public bool IsEngaged { get; set; }
}

// Used by: IntertrialSegmentationError, IntertrialSegmentationSaveError,
//          IntertrialDetectionError, IntertrialDetectionSaveError
public class ApiErrorPayload
{
    public string Error { get; set; } = "";
}

// Used by: IntertrialSegmentationSave, IntertrialDetectionSave
public class ApiLocationPayload
{
    public string Location { get; set; } = "";
}

// Used by: PelletPresentedCountChanged, PelletConsumedCountChanged, ReachCountChanged, SuccessfulReachesCountChanged,
//          DayPelletPresentedCountChanged, DayPelletConsumedCountChanged, DayReachCountChanged, DaySuccessfulReachesCountChanged
public class ApiCountChangedPayload
{
    public int Change { get; set; }
    public int Count { get; set; }
}

// Used by: AutoClampIntensityChanged, AutoClampEngaged, AutoClampPreDisengage, AutoClampDisengaged
public class ApiIntensityPayload
{
    public double Intensity { get; set; }
}

// Used by: TrainingPhaseEnter, TrainingPhaseExit, TrainingProgressUpdate
public class ApiTrainingPhasePayload
{
    public string TrainingPhaseId { get; set; } = "";
}

// AnimalCreated, AnimalUpdated, AnimalSelected use Payload <ApiAnimalStatus> directly (no wrapper class needed).

// ──────────────────────────────────────────────
// Core/System (0000-0999)
// ──────────────────────────────────────────────

public class ApiEmergencyStopPayload
{
    public string Reason { get; set; } = "";
    public List<ApiAlarmKind> ActiveAlarms { get; set; } = [];
}

public class ApiApplicationLaunchedPayload
{
    public string Version { get; set; } = "";
}

public class ApiApplicationModeChangedPayload
{
    public ApiApplicationMode Mode { get; set; }
}

public class ApiPropertyChangedPayload
{
    public int Target { get; set; }
    public string Name { get; set; } = "";
    public JsonElement? NewValue { get; set; }
    public JsonElement? OldValue { get; set; }
}

public class ApiProjectChangedPayload
{
    public string Root { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Day { get; set; } = "";
    public int Session { get; set; }
}

public class ApiProjectSessionChangedPayload
{
    public string Root { get; set; } = "";
    public int Session { get; set; }
}

// ──────────────────────────────────────────────
// Behavior (1000-1999)
// ──────────────────────────────────────────────

public class ApiPelletHomeResetPayload
{
    public int Cycles { get; set; }
}

public class ApiPelletDriftResetPayload
{
    public ApiVector3 Drift { get; set; }
}

public class ApiTrialEndedPayload
{
    public string Result { get; set; } = "";
}

public class ApiBatchAnalysisStartedPayload
{
    public int Count { get; set; }
}

public class ApiBatchAnalysisEndedPayload
{
    public int FailedCount { get; set; }
}

public class ApiHeadfixBaselineChangedPayload
{
    public double Baseline { get; set; }
}

public class ApiAutoClampReleaseToneFreqChangedPayload
{
    public double Frequency { get; set; }
}

public class ApiAutoClampReleaseDelayChangedPayload
{
    public double Delay { get; set; }
}

public class ApiAutoClampPlayReleaseTonePayload
{
    public double Frequency { get; set; }
    public double Duration { get; set; }
}

public class ApiDayStartedPayload
{
    public double Date { get; set; }
}

public class ApiTrialReachEventsPayload
{
    public List<ReachEvent> TrialReachEvents { get; set; } = [];
}

public class ApiIntertrialPelletShiftPayload
{
    public ApiPelletShiftSource Source { get; set; }
    public ApiVector3 Shift { get; set; }
    public bool Deferred { get; set; }
}

// ──────────────────────────────────────────────
// Training (5000-5999)
// ──────────────────────────────────────────────

public class ApiTrainingModeChangedPayload
{
    public ApiTrainingMode TrainingMode { get; set; }
}

public class ApiTrainingPlanLoadPayload
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
}

public static class ReachEventOutcome
{
    public const string None = "none";
    public const string Stalled = "stalled";
    public const string Missed = "missed";
    public const string Dropped = "dropped";
    public const string Grabbed = "grabbed";
    public const string Eaten = "eaten";
}

// From autotrainer.core.ReachEvent
public class ReachEvent
{
    public int Init { get; set; }
    public int? End { get; set; }
    public int? Max { get; set; }
    public string Method { get; set; } = ReachEventMethod.None;
    public string Outcome { get; set; } = ReachEventOutcome.None;
    public double DelaySincePresented { get; set; }
}

public struct ApiVector3
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}
