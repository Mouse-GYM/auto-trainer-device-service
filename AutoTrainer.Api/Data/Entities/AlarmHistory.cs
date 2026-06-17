using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Data.Entities;

public class AlarmHistory : SoftDeleteEntity
{
    public ApiAlarmKind AlarmId { get; set; }
    // Mirrors the API's temporary back-compat field on ApiAlarmStatus.
    public ApiAlarmKind DetectorId { get; set; }
    public bool IsActive { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsAutoResumeEnabled { get; set; }
    public bool IsStopCondition { get; set; }
}
