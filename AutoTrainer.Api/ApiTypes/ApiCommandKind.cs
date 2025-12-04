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

    UserDefined = 99999
}
