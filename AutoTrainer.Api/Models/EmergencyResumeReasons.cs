using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Models;

// Resolves an emergencyResume reason to ApiEmergencyResumeReason, the single representation used
// everywhere: persisted in EmergencyStopHistory, exposed over REST, and carried on hub messages.
//
// This exists only because producers predating the payload's `reason_code` are still in service and send
// nothing but the free-text `reason`. It is a compatibility shim, not a second enum -- once every producer
// sends `reason_code`, the string mapping below can be deleted without touching anything else.
// Source of the strings: agents/emergency-resume-reasons.md.
public static class EmergencyResumeReasons
{
    // Every resume reason is an exact literal. Deliberately no prefix matching: these two share the
    // "alarm-monitor" prefix with the emergencyStop "alarm-monitor: <TOKENS>" form, so a prefix test
    // would pull a stop reason into a resume value.
    public const string AlarmMonitorResumed = "alarm-monitor-resumed";
    public const string AlarmMonitorNoValidConditionRemaining = "alarm-monitor-no-valid-condition-remaining";
    public const string UserButton = "user-button";
    public const string RpcService = "RpcService";

    // Prefers the producer's machine-readable reason_code and falls back to parsing the free-text reason.
    // See EmergencyStopReasons.From for why a null code is not the same as an explicit Unknown.
    public static ApiEmergencyResumeReason From(ApiEmergencyResumeReason? reasonCode, string? reason) =>
        reasonCode ?? FromReason(reason);

    // Ordinal and case-sensitive: RpcService is a mixed-case producer literal. AlarmMonitorStatusChange is
    // the producer's name for the "alarm-monitor-no-valid-condition-remaining" reason.
    public static ApiEmergencyResumeReason FromReason(string? reason) => reason switch
    {
        AlarmMonitorResumed => ApiEmergencyResumeReason.AlarmMonitorResume,
        AlarmMonitorNoValidConditionRemaining => ApiEmergencyResumeReason.AlarmMonitorStatusChange,
        UserButton => ApiEmergencyResumeReason.UserButton,
        RpcService => ApiEmergencyResumeReason.RpcService,
        _ => ApiEmergencyResumeReason.Unknown
    };
}
