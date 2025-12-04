using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Models;

public record Alarm
{
    public ApiAlarmKind AlarmId { get; init; }
    public bool IsActive { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsAutoResumeEnabled { get; init; }
    public bool IsStopCondition { get; init; }
}
