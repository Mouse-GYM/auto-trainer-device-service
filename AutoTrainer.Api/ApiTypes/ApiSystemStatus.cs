namespace AutoTrainer.Api.ApiTypes;

public class ApiSystemStatus
{
    public ApiApplicationMode ApplicationMode { get; set; } = ApiApplicationMode.Idle;
    public ApiTrainingMode TrainingMode { get; set; } = ApiTrainingMode.Undefined;
    public ApiAnimalStatus? Animal { get; set; }
    public ApiProjectStatus Project { get; set; }
    public List<ApiAlarmStatus> Alarms { get; set; } = [];
    public List<ApiDetectorStatus> Detectors { get; set; } = [];
    public ApiPelletStatus PelletDevice { get; set; }
    public ApiTunnelStatus TunnelDevice { get; set; }
    public ApiBehaviorStatus Behavior { get; set; }

    public override string ToString()
    {
        var animal = Animal is { } a ? $", Animal={a.Name}" : "";
        var project = !string.IsNullOrEmpty(Project.DayPath) ? $", DayPath={Project.DayPath}" : "";

        return $"ApplicationMode={ApplicationMode}, TrainingMode={TrainingMode}{animal}{project}";
    }
}

public readonly record struct ApiProjectStatus
{
    // Absolute path to the current day's directory; the YYYYMMDD day is its final component.
    public string DayPath { get; init; }
    public int TrialId { get; init; }
    public string SessionId { get; init; }
}

public readonly record struct ApiReachStatus
{
    public int PelletsPresented { get; init; }
    public int PelletsConsumed { get; init; }
    public int Reaches { get; init; }
    public int SuccessfulReaches { get; init; }
}

public readonly record struct ApiBehaviorStatus
{
    public double BaselineMagnetIntensity { get; init; }
    public ApiReachStatus Reaches { get; init; }
}

public readonly record struct ApiAnimalStatus
{
    public string Identifier { get; init; }
    public string Name { get; init; }
    public double DcsSendX { get; init; }
    public double DcsSendY { get; init; }
    public double DcsSendZ { get; init; }
    public double? TargetYLimit { get; init; }
    public ApiReachStatus ReachStatusTotal { get; init; }
    public ApiReachStatus ReachStatusDay { get; init; }
}

public readonly record struct ApiDetectorStatus
{
    public ApiDetectorKind DetectorId { get; init; }
    public bool IsActive { get; init; }
    public bool IsEnabled { get; init; }
}

public readonly record struct ApiAlarmStatus
{
    public ApiAlarmKind AlarmId { get; init; }
    // TODO Temporary until updated application has been out awhile.
    public ApiAlarmKind DetectorId { get; init; }
    public bool IsActive { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsAutoResumeEnabled { get; init; }
    public bool IsStopCondition { get; init; }
}
