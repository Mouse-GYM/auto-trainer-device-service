namespace AutoTrainer.Api.Models;

// The reason an emergencyResume event was posted, converted from the producer's `reason` string.
// Authored here ahead of the producer: these members and values are what the Python API is expected to
// adopt when it starts sending the enum on the payload, so the numbering is deliberate and gapped.
// UserButton and RpcService deliberately share their values with EmergencyStopReason -- the same trigger
// seen from the other direction. Source of the strings: agents/emergency-resume-reasons.md.
public enum EmergencyResumeReason
{
    Unknown = 0,
    AlarmMonitorResumed = 101,
    AlarmMonitorNoValidConditionRemaining = 102,
    UserButton = 201,
    RpcService = 301
}

public static class EmergencyResumeReasons
{
    // Every resume reason is an exact literal. Deliberately no prefix matching: these two share the
    // "alarm-monitor" prefix with the emergencyStop "alarm-monitor: <TOKENS>" form, so a prefix test
    // would pull a stop reason into a resume value.
    public const string AlarmMonitorResumed = "alarm-monitor-resumed";
    public const string AlarmMonitorNoValidConditionRemaining = "alarm-monitor-no-valid-condition-remaining";
    public const string UserButton = "user-button";
    public const string RpcService = "RpcService";

    // Ordinal and case-sensitive: RpcService is a mixed-case producer literal.
    public static EmergencyResumeReason FromReason(string? reason) => reason switch
    {
        AlarmMonitorResumed => EmergencyResumeReason.AlarmMonitorResumed,
        AlarmMonitorNoValidConditionRemaining => EmergencyResumeReason.AlarmMonitorNoValidConditionRemaining,
        UserButton => EmergencyResumeReason.UserButton,
        RpcService => EmergencyResumeReason.RpcService,
        _ => EmergencyResumeReason.Unknown
    };
}
