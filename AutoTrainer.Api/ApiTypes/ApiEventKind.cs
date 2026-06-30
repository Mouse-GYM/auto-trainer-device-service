namespace AutoTrainer.Api.ApiTypes;

public enum ApiEventKind
{
    // Core/System (0000-0999)
    EmergencyStop = 101,
    EmergencyResume = 102,

    ApplicationLaunched = 201,
    ApplicationTerminating = 202,

    ApplicationModeChanged = 301,
    CalibrationDcsCompleted = 311,
    CalibrationDcsFailed = 312,
    Calibration3dCompleted = 321,
    Calibration3dFailed = 322,

    PropertyChanged = 501,

    SystemStatus = 601,

    ProjectChanged = 701,
    ProjectTrialChanged = 702,

    // Behavior (1000-1999)
    AlgorithmPause = 1001,
    AlgorithmResume = 1002,

    TunnelEnter = 1101,
    TunnelExit = 1102,

    PelletLoadBegin = 1202,
    PelletLoadEnd = 1203,

    PelletSendBegin = 1205,
    PelletSendEnd = 1206,

    PelletCoverBegin = 1208,
    PelletCoverEnd = 1209,

    PelletReleaseBegin = 1211,
    PelletReleaseEnd = 1212,

    PelletHomeBegin = 1214,
    PelletHomeEnd = 1215,

    PelletRetractBegin = 1220,
    PelletRetractEnd = 1221,

    PelletHomeReset = 1222,
    PelletDriftReset = 1223,

    TunnelGateOpenBegin = 1230,
    TunnelGateOpenEnd = 1231,
    TunnelGateCloseBegin = 1232,
    TunnelGateCloseEnd = 1233,

    TunnelFanOnBegin = 1235,
    TunnelFanOnEnd = 1236,
    TunnelFanOffBegin = 1237,
    TunnelFanOffEnd = 1238,

    TrialStarted = 1301,
    TrialCaptureEnded = 1305,
    TrialEnded = 1311,

    BatchAnalysisStarted = 1321,
    BatchAnalysisEnded = 1322,

    SessionStarted = 1331,
    SessionEnded = 1332,

    HeadfixLoadCellEnabledChanged = 1401,
    HeadfixLoadCellChangedInIntersession = 1405,
    HeadfixLoadCellChangedWrongState = 1406,
    HeadfixAutoTare = 1411,

    HeadFixationForceDetectorChanged = 1421,

    TrialAnimalSeen = 1501,
    TrialRightHandSeen = 1502,
    TrialPelletSeen = 1503,

    HeadfixBaselineChanged = 1601,

    AutoClampEnabledChanged = 1611,
    AutoClampIntensityChanged = 1612,
    AutoClampReleaseToneFreqChanged = 1613,
    AutoClampReleaseDelayChanged = 1614,

    AutoClampEngaged = 1621,
    AutoClampPreDisengage = 1622,
    AutoClampPlayReleaseTone = 1623,
    AutoClampDisengaged = 1624,

    IntertrialSegmentationBegin = 1701,
    IntertrialSegmentationEnd = 1702,
    IntertrialSegmentationError = 1703,
    IntertrialSegmentationSave = 1704,
    IntertrialSegmentationSaveError = 1705,

    IntertrialDetectionBegin = 1711,
    IntertrialDetectionEnd = 1712,
    IntertrialDetectionError = 1713,
    IntertrialDetectionSave = 1714,
    IntertrialDetectionSaveError = 1715,

    PelletPresentedCountChanged = 1800,
    PelletConsumedCountChanged = 1802,
    ReachCountChanged = 1803,
    SuccessfulReachesCountChanged = 1804,

    DayStarted = 1811,
    DayPelletPresentedCountChanged = 1812,
    DayPelletConsumedCountChanged = 1813,
    DayReachCountChanged = 1814,
    DaySuccessfulReachesCountChanged = 1815,

    TrialPelletPresented = 1901,
    TrialReachEvents = 1911,

    IntertrialPelletShift = 1921,

    // Device (2000-2999)
    DeviceCommandSend = 2001,
    DeviceCommandAcknowledge = 2002,

    // Analysis (4000-4999)
    LoadCellEngagedChanged = 4001,
    HeadbarPressureEngagedChanged = 4011,

    // Training (5000-5999)
    TrainingModeChanged = 5001,
    TrainingPlanLoad = 5101,
    TrainingPhaseEnter = 5201,
    TrainingPhaseExit = 5202,
    TrainingProgressUpdate = 5501,

    // Training (internal behavior, 5800-5899)
    ProtocolEvent = 5801,
    ProtocolPlanEvent = 5811,
    ProtocolPhaseEvent = 5821,
    ProtocolPredicateEvent = 5831,
    ProtocolActionEvent = 5841,
    ProtocolProgressEvent = 5851,

    // Detectors & Alarms (6000-6999)
    DetectorChanged = 6001,
    AlarmChanged = 6501,

    // Animal (7000-7999)
    AnimalCreated = 7001,
    AnimalUpdated = 7002,
    AnimalSelected = 7003,

    // Utility
    Unknown = 999999
}
