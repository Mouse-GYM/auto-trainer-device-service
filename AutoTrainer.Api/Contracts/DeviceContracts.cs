using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Models;

namespace AutoTrainer.Api.Contracts;

// ObservedAt is the row's CreatedAt: for a time-series row the observation time IS the datum, so it stays on
// the wire even though other audit columns (UpdatedAt/DeletedAt) are dropped from DTOs.
public sealed record AlarmDto(int Id, DateTime ObservedAt, ApiAlarmKind AlarmId,
    bool IsActive, bool IsEnabled, bool IsAutoResumeEnabled, bool IsStopCondition);

public sealed record DetectorDto(int Id, DateTime ObservedAt, ApiDetectorKind DetectorId,
    bool IsActive, bool IsEnabled);

// One emergencyStop / emergencyResume event. Unlike the alarm/detector rows, these carry a producer
// timestamp, so OccurredAt is the event time rather than the row's write time.
//
// Exactly one of StopReasonId / ResumeReasonId is set, per Kind. ReasonText is always the verbatim producer
// string -- read it when the reason id is Unknown, and to recover the "alarm-monitor:" token suffix that the
// enum deliberately collapses.
//
// ActiveAlarms is the alarm set the producer reported at the stop; it is null on resume rows, which carry no
// such field. An empty array means the stop reported no active alarm. It is the alarm *ids* only -- query
// /device/alarms for the state of any individual alarm at that time.
//
// NotificationSentAt is null when no operator notification was delivered for this event: notifications are
// not configured, the event never reached the notification path, or publishing failed.
public sealed record EmergencyDto(int Id, DateTime OccurredAt, ApiEventKind Kind,
    EmergencyStopReason? StopReasonId, EmergencyResumeReason? ResumeReasonId, string ReasonText,
    IReadOnlyList<ApiAlarmKind>? ActiveAlarms, DateTime? NotificationSentAt);
