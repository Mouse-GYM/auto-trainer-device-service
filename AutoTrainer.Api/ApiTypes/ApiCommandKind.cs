namespace AutoTrainer.Api.ApiTypes;

public enum ApiCommandKind
{
    None = 0,

    [Obsolete("Obsolete in Python API.")]
    StartAcquisition = 50,
    [Obsolete("Obsolete in Python API.")]
    StopAcquisition = 55,

    SetApplicationMode = 120,
    SetTrainingMode = 130,

    EmergencyStop = 200,
    EmergencyResume = 210,

    GetConfiguration = 300,
    GetStatus = 400,
    GetAnimals = 500,

    // Behavior settings (1000-1099). All share one shape: {enabled: bool} in and out, the response carrying
    // the state actually in effect after the attempt. Each is read back as the like-named Is*Enabled field of
    // ApiBehaviorStatus or ApiPelletStatus. Values are spaced by ten to leave room for the sub-settings each
    // one gates.
    SetLiveAnalysisEnabled = 1000,
    SetPelletDeliveryEnabled = 1010,
    SetPelletCoverEnabled = 1020,
    SetIntertrialPelletShiftEnabled = 1030,
    SetHomeOnExcessiveDriftEnabled = 1040,
    SetTrianglePelletDistanceDetectionEnabled = 1050,
    SetAutoCloseGateOnIntertrialEnabled = 1060,
    SetAutoClampEnabled = 1070,
    SetTunnelSweepEnabled = 1080,
    SetBatchTrialsEnabled = 1090,

    UserDefined = 99999
}
