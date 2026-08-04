namespace AutoTrainer.Api.ApiTypes;

/// <summary>
/// The authoritative kind -> payload-type map for <see cref="ApiEvent"/>.
///
/// An event carries no type tag: <see cref="ApiEvent.Context"/> is an untyped bag and <see cref="ApiEvent.Kind"/>
/// is the ONLY discriminator. This map is the C# mirror of the <c>build_event</c> overload set in
/// <c>autotrainer/api/event/build_event.py</c>, which is the Python source of truth (mypy enforces that every
/// ApiEventKind appears there exactly once).
///
/// Every <see cref="ApiEventKind"/> must have an entry here; <c>null</c> means the event has no payload.
/// ApiEventPayloadMapTests asserts that exhaustively, so a kind added by a future sync-api-types run fails the
/// test until it is mapped rather than being silently ignored at runtime.
/// </summary>
public static class ApiEventPayloadMap
{
    private static readonly Dictionary<ApiEventKind, Type?> s_ForKind = new()
    {
        [ApiEventKind.EmergencyStop] = typeof(ApiEmergencyStopPayload),
        [ApiEventKind.EmergencyResume] = typeof(ApiEmergencyResumePayload),
        [ApiEventKind.ApplicationLaunched] = typeof(ApiVersionPayload),
        [ApiEventKind.ApplicationTerminating] = typeof(ApiReasonPayload),
        [ApiEventKind.ApplicationModeChanged] = typeof(ApiApplicationModePayload),
        [ApiEventKind.CalibrationDcsCompleted] = null,
        [ApiEventKind.CalibrationDcsFailed] = typeof(ApiReasonPayload),
        [ApiEventKind.Calibration3dCompleted] = null,
        [ApiEventKind.Calibration3dFailed] = typeof(ApiReasonPayload),
        [ApiEventKind.PropertyChanged] = typeof(ApiPropertyChangedPayload),
        [ApiEventKind.SystemStatus] = typeof(ApiSystemStatus),
        [ApiEventKind.ProjectChanged] = typeof(ApiProjectChangedPayload),
        [ApiEventKind.ProjectTrialChanged] = typeof(ApiProjectTrialChangedPayload),
        [ApiEventKind.AlgorithmPause] = null,
        [ApiEventKind.AlgorithmResume] = null,
        [ApiEventKind.TunnelEnter] = null,
        [ApiEventKind.TunnelExit] = null,
        [ApiEventKind.PelletLoadBegin] = typeof(ApiCommandPayload),
        [ApiEventKind.PelletLoadEnd] = typeof(ApiCommandPayload),
        [ApiEventKind.PelletSendBegin] = typeof(ApiCommandPayload),
        [ApiEventKind.PelletSendEnd] = typeof(ApiCommandPayload),
        [ApiEventKind.PelletCoverBegin] = typeof(ApiCommandPayload),
        [ApiEventKind.PelletCoverEnd] = typeof(ApiCommandPayload),
        [ApiEventKind.PelletReleaseBegin] = typeof(ApiCommandPayload),
        [ApiEventKind.PelletReleaseEnd] = typeof(ApiCommandPayload),
        [ApiEventKind.PelletHomeBegin] = typeof(ApiCommandPayload),
        [ApiEventKind.PelletHomeEnd] = typeof(ApiCommandPayload),
        [ApiEventKind.PelletRetractBegin] = typeof(ApiCommandPayload),
        [ApiEventKind.PelletRetractEnd] = typeof(ApiCommandPayload),
        [ApiEventKind.PelletHomeReset] = typeof(ApiPelletHomeResetPayload),
        [ApiEventKind.PelletDriftReset] = typeof(ApiPelletDriftResetPayload),
        [ApiEventKind.TunnelGateOpenBegin] = typeof(ApiCommandPayload),
        [ApiEventKind.TunnelGateOpenEnd] = typeof(ApiCommandPayload),
        [ApiEventKind.TunnelGateCloseBegin] = typeof(ApiCommandPayload),
        [ApiEventKind.TunnelGateCloseEnd] = typeof(ApiCommandPayload),
        [ApiEventKind.TunnelFanOnBegin] = typeof(ApiCommandPayload),
        [ApiEventKind.TunnelFanOnEnd] = typeof(ApiCommandPayload),
        [ApiEventKind.TunnelFanOffBegin] = typeof(ApiCommandPayload),
        [ApiEventKind.TunnelFanOffEnd] = typeof(ApiCommandPayload),
        [ApiEventKind.SessionStarted] = typeof(ApiSessionStartedPayload),
        [ApiEventKind.SessionEnded] = typeof(ApiSessionEndedPayload),
        [ApiEventKind.TrialStarted] = typeof(ApiTrialStartedPayload),
        [ApiEventKind.TrialCaptureEnded] = typeof(ApiSessionTrialPayload),
        [ApiEventKind.TrialEnded] = typeof(ApiTrialEndedPayload),
        [ApiEventKind.BatchAnalysisStarted] = typeof(ApiBatchAnalysisStartedPayload),
        [ApiEventKind.BatchAnalysisEnded] = typeof(ApiBatchAnalysisEndedPayload),
        [ApiEventKind.HeadfixLoadCellEnabledChanged] = typeof(ApiIsEnabledPayload),
        [ApiEventKind.HeadfixLoadCellChangedInIntersession] = typeof(ApiIsEnabledPayload),
        [ApiEventKind.HeadfixLoadCellChangedWrongState] = typeof(ApiIsEnabledPayload),
        [ApiEventKind.HeadfixAutoTare] = null,
        [ApiEventKind.HeadFixationForceDetectorChanged] = typeof(ApiIsEnabledPayload),
        [ApiEventKind.TrialAnimalSeen] = typeof(ApiTrialSeenPayload),
        [ApiEventKind.TrialRightHandSeen] = typeof(ApiTrialSeenPayload),
        [ApiEventKind.TrialPelletSeen] = typeof(ApiTrialSeenPayload),
        [ApiEventKind.HeadfixBaselineChanged] = typeof(ApiBaselinePayload),
        [ApiEventKind.AutoClampEnabledChanged] = typeof(ApiIsEnabledPayload),
        [ApiEventKind.AutoClampIntensityChanged] = typeof(ApiIntensityPayload),
        [ApiEventKind.AutoClampReleaseToneFreqChanged] = typeof(ApiFrequencyPayload),
        [ApiEventKind.AutoClampReleaseDelayChanged] = typeof(ApiDelayPayload),
        [ApiEventKind.AutoClampEngaged] = typeof(ApiIntensityPayload),
        [ApiEventKind.AutoClampPreDisengage] = typeof(ApiIntensityPayload),
        [ApiEventKind.AutoClampPlayReleaseTone] = typeof(ApiReleaseTonePayload),
        [ApiEventKind.AutoClampDisengaged] = typeof(ApiIntensityPayload),
        [ApiEventKind.IntertrialSegmentationBegin] = typeof(ApiAnalysisTrialPayload),
        [ApiEventKind.IntertrialSegmentationEnd] = typeof(ApiAnalysisTrialPayload),
        [ApiEventKind.IntertrialSegmentationError] = typeof(ApiIntertrialErrorPayload),
        [ApiEventKind.IntertrialSegmentationSave] = typeof(ApiIntertrialSavePayload),
        [ApiEventKind.IntertrialSegmentationSaveError] = typeof(ApiIntertrialErrorPayload),
        [ApiEventKind.IntertrialDetectionBegin] = typeof(ApiAnalysisTrialPayload),
        [ApiEventKind.IntertrialDetectionEnd] = typeof(ApiAnalysisTrialPayload),
        [ApiEventKind.IntertrialDetectionError] = typeof(ApiIntertrialErrorPayload),
        [ApiEventKind.IntertrialDetectionSave] = typeof(ApiIntertrialSavePayload),
        [ApiEventKind.IntertrialDetectionSaveError] = typeof(ApiIntertrialErrorPayload),
        [ApiEventKind.IntertrialResponse] = typeof(ApiIntertrialResponsePayload),
        [ApiEventKind.PelletPresentedCountChanged] = typeof(ApiCountChangePayload),
        [ApiEventKind.PelletConsumedCountChanged] = typeof(ApiCountChangePayload),
        [ApiEventKind.ReachCountChanged] = typeof(ApiCountChangePayload),
        [ApiEventKind.SuccessfulReachesCountChanged] = typeof(ApiCountChangePayload),
        [ApiEventKind.DayStarted] = typeof(ApiDayStartedPayload),
        [ApiEventKind.DayPelletPresentedCountChanged] = typeof(ApiCountChangePayload),
        [ApiEventKind.DayPelletConsumedCountChanged] = typeof(ApiCountChangePayload),
        [ApiEventKind.DayReachCountChanged] = typeof(ApiCountChangePayload),
        [ApiEventKind.DaySuccessfulReachesCountChanged] = typeof(ApiCountChangePayload),
        [ApiEventKind.TrialPelletPresented] = typeof(ApiSessionTrialPayload),
        [ApiEventKind.TrialReachEvents] = typeof(ApiTrialReachEventsPayload),
        [ApiEventKind.IntertrialPelletShift] = typeof(ApiPelletShiftPayload),
        [ApiEventKind.DeviceCommandSend] = typeof(ApiCommandPayload),
        [ApiEventKind.DeviceCommandAcknowledge] = typeof(ApiCommandPayload),
        [ApiEventKind.LoadCellEngagedChanged] = typeof(ApiIsEngagedPayload),
        [ApiEventKind.HeadbarPressureEngagedChanged] = typeof(ApiIsEngagedPayload),
        [ApiEventKind.TrainingModeChanged] = typeof(ApiTrainingModePayload),
        [ApiEventKind.TrainingPlanLoad] = typeof(ApiTrainingPlanPayload),
        [ApiEventKind.TrainingPhaseEnter] = typeof(ApiTrainingPhasePayload),
        [ApiEventKind.TrainingPhaseExit] = typeof(ApiTrainingPhasePayload),
        [ApiEventKind.TrainingProgressUpdate] = typeof(ApiTrainingPhasePayload),
        [ApiEventKind.ProtocolEvent] = typeof(ApiProtocolEventPayload),
        [ApiEventKind.ProtocolPlanEvent] = typeof(ApiProtocolEventPayload),
        [ApiEventKind.ProtocolPhaseEvent] = typeof(ApiProtocolEventPayload),
        [ApiEventKind.ProtocolPredicateEvent] = typeof(ApiProtocolEventPayload),
        [ApiEventKind.ProtocolActionEvent] = typeof(ApiProtocolEventPayload),
        [ApiEventKind.ProtocolProgressEvent] = typeof(ApiProtocolEventPayload),
        [ApiEventKind.DetectorChanged] = typeof(ApiDetectorStatus),
        [ApiEventKind.AlarmChanged] = typeof(ApiAlarmStatus),
        [ApiEventKind.AnimalCreated] = typeof(ApiAnimalStatus),
        [ApiEventKind.AnimalUpdated] = typeof(ApiAnimalStatus),
        [ApiEventKind.AnimalSelected] = typeof(ApiAnimalStatus),
        [ApiEventKind.Unknown] = null,    };

    /// <summary>Payload type for each event kind; the value is null when the kind has no payload.</summary>
    public static IReadOnlyDictionary<ApiEventKind, Type?> ForKind => s_ForKind;

    /// <summary>
    /// The payload type for <paramref name="kind"/>, or null when the kind has no payload or is unknown
    /// (an unrecognized value off the wire).
    /// </summary>
    public static Type? PayloadType(ApiEventKind kind) =>
        s_ForKind.TryGetValue(kind, out var type) ? type : null;

    /// <summary>Every distinct payload type a consumer of the event stream must be able to deserialize.</summary>
    public static IReadOnlyCollection<Type> AllPayloadTypes { get; } =
        [.. s_ForKind.Values.OfType<Type>().Distinct()];
}
