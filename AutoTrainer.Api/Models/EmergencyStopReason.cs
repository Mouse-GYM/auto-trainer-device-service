namespace AutoTrainer.Api.Models;

// The reason an emergencyStop event was posted, converted from the producer's `reason` string.
// Authored here ahead of the producer: these members and values are what the Python API is expected to
// adopt when it starts sending the enum on the payload, so the numbering is deliberate and gapped.
// Source of the strings: agents/emergency-stop-reasons.md.
public enum EmergencyStopReason
{
    Unknown = 0,
    AlarmMonitor = 101,
    UserButton = 201,
    RpcService = 301,
    DiamondCoordCheck = 401
}

public static class EmergencyStopReasons
{
    // The producer builds this as f"alarm-monitor: {reasons}", so the colon is always present. The colon
    // is what distinguishes it from the emergencyResume "alarm-monitor-*" literals -- matching on a bare
    // "alarm-monitor" prefix would swallow those.
    public const string AlarmMonitorPrefix = "alarm-monitor:";
    public const string UserButton = "user-button";
    public const string RpcService = "RpcService";
    public const string DiamondCoordCheck = "Diamond-Coord-Check";

    // Ordinal and case-sensitive: RpcService and Diamond-Coord-Check are mixed-case producer literals.
    public static EmergencyStopReason FromReason(string? reason) => reason switch
    {
        UserButton => EmergencyStopReason.UserButton,
        RpcService => EmergencyStopReason.RpcService,
        DiamondCoordCheck => EmergencyStopReason.DiamondCoordCheck,
        not null when reason.StartsWith(AlarmMonitorPrefix, StringComparison.Ordinal)
            => EmergencyStopReason.AlarmMonitor,
        _ => EmergencyStopReason.Unknown
    };
}
