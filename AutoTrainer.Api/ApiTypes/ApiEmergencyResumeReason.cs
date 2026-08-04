namespace AutoTrainer.Api.ApiTypes;

// Mirrors ApiEmergencyResumeReason in autotrainer/api/api_emergency_stop.py.
//
// AlarmMonitorStatusChange is the enum form of the "alarm-monitor-no-valid-condition-remaining" reason
// string (per the Python source comment).
//
// This is the single representation of a resume reason: it is what EmergencyStopHistory.ResumeReasonId
// persists, what /device/emergencies returns and filters on, and what hub messages carry. A producer that
// does not yet send `reason_code` is resolved to it by EmergencyResumeReasons.FromReason.
public enum ApiEmergencyResumeReason
{
    Unknown = 0,
    AlarmMonitorResume = 101,
    AlarmMonitorStatusChange = 201,
    UserButton = 301,
    RpcService = 401
}
