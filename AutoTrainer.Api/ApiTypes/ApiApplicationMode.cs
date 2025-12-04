namespace AutoTrainer.Api.ApiTypes;

public enum ApiApplicationMode
{
    Idle = 0,
    Running = 100,
    InDevice = 200,
    InTraining = 300,
    CalibrationDcs = 1000,
    Calibration3d = 1100
}
