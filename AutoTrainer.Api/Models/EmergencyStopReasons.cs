using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Models;

// Resolves an emergencyStop reason to ApiEmergencyStopReason, the single representation used everywhere:
// persisted in EmergencyStopHistory, exposed over REST, and carried on hub messages.
//
// This exists only because producers predating the payload's `reason_code` are still in service and send
// nothing but the free-text `reason`. It is a compatibility shim, not a second enum -- once every producer
// sends `reason_code`, the string mapping below can be deleted without touching anything else.
// Source of the strings: agents/emergency-stop-reasons.md.
public static class EmergencyStopReasons
{
    // The producer builds this as f"alarm-monitor: {reasons}", so the colon is always present. The colon
    // is what distinguishes it from the emergencyResume "alarm-monitor-*" literals -- matching on a bare
    // "alarm-monitor" prefix would swallow those.
    public const string AlarmMonitorPrefix = "alarm-monitor:";
    public const string UserButton = "user-button";
    public const string RpcService = "RpcService";
    public const string DiamondCoordCheck = "Diamond-Coord-Check";

    // Prefers the producer's machine-readable reason_code and falls back to parsing the free-text reason.
    // A null code means "this producer does not send reason_code", which is not the same as an explicit
    // ApiEmergencyStopReason.Unknown ("classified, and the source was not a known member") -- an explicit
    // Unknown is honoured as-is rather than re-derived from the string.
    public static ApiEmergencyStopReason From(ApiEmergencyStopReason? reasonCode, string? reason) =>
        reasonCode ?? FromReason(reason);

    // Ordinal and case-sensitive: RpcService and Diamond-Coord-Check are mixed-case producer literals.
    public static ApiEmergencyStopReason FromReason(string? reason) => reason switch
    {
        UserButton => ApiEmergencyStopReason.UserButton,
        RpcService => ApiEmergencyStopReason.RpcService,
        DiamondCoordCheck => ApiEmergencyStopReason.DiamondCoordCheck,
        not null when reason.StartsWith(AlarmMonitorPrefix, StringComparison.Ordinal)
            => ApiEmergencyStopReason.AlarmMonitor,
        _ => ApiEmergencyStopReason.Unknown
    };
}
