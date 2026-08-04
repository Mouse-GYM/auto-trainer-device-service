namespace AutoTrainer.Api.ApiTypes;

// Mirrors ApiEmergencyStopReason in autotrainer/api/api_emergency_stop.py.
//
// This is the single representation of a stop reason: it is what EmergencyStopHistory.StopReasonId
// persists, what /device/emergencies returns and filters on, and what hub messages carry. A producer that
// does not yet send `reason_code` is resolved to it by EmergencyStopReasons.FromReason.
public enum ApiEmergencyStopReason
{
    Unknown = 0,
    AlarmMonitor = 101,
    DiamondCoordCheck = 201,
    UserButton = 301,
    RpcService = 401
}
