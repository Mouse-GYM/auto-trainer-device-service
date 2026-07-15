using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Contracts;

// ObservedAt is the row's CreatedAt: for a time-series row the observation time IS the datum, so it stays on
// the wire even though other audit columns (UpdatedAt/DeletedAt) are dropped from DTOs.
public sealed record AlarmDto(int Id, DateTime ObservedAt, ApiAlarmKind AlarmId,
    bool IsActive, bool IsEnabled, bool IsAutoResumeEnabled, bool IsStopCondition);

public sealed record DetectorDto(int Id, DateTime ObservedAt, ApiDetectorKind DetectorId,
    bool IsActive, bool IsEnabled);
