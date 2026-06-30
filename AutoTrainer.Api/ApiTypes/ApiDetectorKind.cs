namespace AutoTrainer.Api.ApiTypes;

public enum ApiDetectorKind
{
    FrontDoor = 101,
    SlidingDoor = 102,
    LoadCellThrash = 201,
    AudioThrash = 202,
    PelletMisplaced = 301,
    DeviceAckTimeOut = 401,

    PelletStatusMessageInterruption = 411,
    TunnelStatusMessageInterruption = 412,

    LowFreeDiskSpace = 501,
    MainThreadFrozen = 511,

    PelletRefillCountExceeded = 601,
    CageCleaningRequired = 610,
    ConsecutivePelletLoadFailureExceeded = 620,

    // Training Issues (non-emergency)
    AnimalAutoClampEvasion = 701,

    // Animal Welfare (non-emergency)
    AnimalLowPelletsConsumedPerDay = 801,

    [Obsolete("Deprecated in the Python API.")]
    AnimalImmobile = 99999
}
