namespace AutoTrainer.Api.ApiTypes;

public enum ApiAlarmKind
{
    // Environmental
    ExternalDoors = 101,

    // Animal
    AnimalMissing = 201,
    AnimalImmobile = 210,

    // Behavior
    Thrashing = 301,

    // Device hardware
    DeviceCommunication = 401,

    // Jetson/Computer
    SystemFault = 501,

    // System maintenance
    SystemMaintenance = 601,

    // Training Issues (non-emergency)
    AnimalEvasion = 701,

    // Animal Welfare (non-emergency)
    AnimalWelfare = 801
}
