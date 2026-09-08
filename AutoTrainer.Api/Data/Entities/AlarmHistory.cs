using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Data.Entities;

public class AlarmHistory : SoftDeleteEntity
{
    public ApiAlarmKind AlarmId { get; set; }
    public bool IsActive { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsAutoResumeEnabled { get; set; }
    public bool IsStopCondition { get; set; }
}
