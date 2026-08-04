using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Models;
using Xunit;

namespace AutoTrainer.Api.Tests.Models;

// The producer strings are documented in agents/emergency-stop-reasons.md and
// agents/emergency-resume-reasons.md, and are exhaustive for non-test producer code.
public class EmergencyReasonTests
{
    [Theory]
    [InlineData("user-button", ApiEmergencyStopReason.UserButton)]
    [InlineData("RpcService", ApiEmergencyStopReason.RpcService)]
    [InlineData("Diamond-Coord-Check", ApiEmergencyStopReason.DiamondCoordCheck)]
    // Every alarm-monitor variant collapses to one member: the suffix is variable and unordered, and the
    // payload's active_alarms already carries that information structurally.
    [InlineData("alarm-monitor: DOORS_OPEN", ApiEmergencyStopReason.AlarmMonitor)]
    [InlineData("alarm-monitor: DOORS_OPEN SYSTEM_FAULT MOUSE_THRASHING", ApiEmergencyStopReason.AlarmMonitor)]
    [InlineData("alarm-monitor: ", ApiEmergencyStopReason.AlarmMonitor)]
    [InlineData("alarm-monitor:", ApiEmergencyStopReason.AlarmMonitor)]
    public void StopReason_MapsDocumentedStrings(string reason, ApiEmergencyStopReason expected) =>
        Assert.Equal(expected, EmergencyStopReasons.FromReason(reason));

    [Theory]
    [InlineData("alarm-monitor-resumed", ApiEmergencyResumeReason.AlarmMonitorResume)]
    [InlineData("alarm-monitor-no-valid-condition-remaining",
        ApiEmergencyResumeReason.AlarmMonitorStatusChange)]
    [InlineData("user-button", ApiEmergencyResumeReason.UserButton)]
    [InlineData("RpcService", ApiEmergencyResumeReason.RpcService)]
    public void ResumeReason_MapsDocumentedStrings(string reason, ApiEmergencyResumeReason expected) =>
        Assert.Equal(expected, EmergencyResumeReasons.FromReason(reason));

    // The whole point of two separate converters. The stop form carries a colon; the two resume literals
    // share the "alarm-monitor" prefix without one, so a bare-prefix test would cross-contaminate.
    [Theory]
    [InlineData("alarm-monitor-resumed")]
    [InlineData("alarm-monitor-no-valid-condition-remaining")]
    public void StopReason_DoesNotMatchResumeAlarmMonitorLiterals(string resumeReason) =>
        Assert.Equal(ApiEmergencyStopReason.Unknown, EmergencyStopReasons.FromReason(resumeReason));

    [Theory]
    [InlineData("alarm-monitor: DOORS_OPEN")]
    [InlineData("Diamond-Coord-Check")]
    public void ResumeReason_DoesNotMatchStopOnlyReasons(string stopReason) =>
        Assert.Equal(ApiEmergencyResumeReason.Unknown, EmergencyResumeReasons.FromReason(stopReason));

    // RpcService and Diamond-Coord-Check are mixed-case producer literals; matching must stay ordinal.
    [Theory]
    [InlineData("rpcservice")]
    [InlineData("RPCSERVICE")]
    [InlineData("USER-BUTTON")]
    [InlineData("diamond-coord-check")]
    [InlineData("Alarm-Monitor: DOORS_OPEN")]
    public void StopReason_IsCaseSensitive(string reason) =>
        Assert.Equal(ApiEmergencyStopReason.Unknown, EmergencyStopReasons.FromReason(reason));

    [Theory]
    [InlineData("rpcservice")]
    [InlineData("USER-BUTTON")]
    [InlineData("ALARM-MONITOR-RESUMED")]
    public void ResumeReason_IsCaseSensitive(string reason) =>
        Assert.Equal(ApiEmergencyResumeReason.Unknown, EmergencyResumeReasons.FromReason(reason));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something-the-producer-added-later")]
    public void UnrecognizedStrings_MapToUnknown(string? reason)
    {
        Assert.Equal(ApiEmergencyStopReason.Unknown, EmergencyStopReasons.FromReason(reason));
        Assert.Equal(ApiEmergencyResumeReason.Unknown, EmergencyResumeReasons.FromReason(reason));
    }

    // ---- reason_code, with string fallback ---------------------------------

    // Producers predating reason_code are still in service, so a null code must fall back to the string.
    [Fact]
    public void NullReasonCode_FallsBackToTheReasonString()
    {
        Assert.Equal(ApiEmergencyStopReason.AlarmMonitor,
            EmergencyStopReasons.From(null, "alarm-monitor: DOORS_OPEN"));
        Assert.Equal(ApiEmergencyResumeReason.AlarmMonitorResume,
            EmergencyResumeReasons.From(null, "alarm-monitor-resumed"));
    }

    // When the code is present it wins outright -- even over a string that would parse to something else.
    [Fact]
    public void PresentReasonCode_TakesPrecedenceOverTheString()
    {
        Assert.Equal(ApiEmergencyStopReason.UserButton,
            EmergencyStopReasons.From(ApiEmergencyStopReason.UserButton, "alarm-monitor: DOORS_OPEN"));
        Assert.Equal(ApiEmergencyResumeReason.RpcService,
            EmergencyResumeReasons.From(ApiEmergencyResumeReason.RpcService, "user-button"));
    }

    // An explicit Unknown means "the producer classified this and did not recognize the source" -- it is a
    // real answer, not a missing one, so it must NOT fall through to the string.
    [Fact]
    public void ExplicitUnknownCode_IsHonoured_NotTreatedAsMissing()
    {
        Assert.Equal(ApiEmergencyStopReason.Unknown,
            EmergencyStopReasons.From(ApiEmergencyStopReason.Unknown, "user-button"));
        Assert.Equal(ApiEmergencyResumeReason.Unknown,
            EmergencyResumeReasons.From(ApiEmergencyResumeReason.Unknown, "user-button"));
    }



    // Mirrors autotrainer/api/api_emergency_stop.py. These are wire values -- drift breaks both repos.
    [Fact]
    public void ApiEnumValues_MirrorThePythonProducer()
    {
        Assert.Equal(0, (int)ApiEmergencyStopReason.Unknown);
        Assert.Equal(101, (int)ApiEmergencyStopReason.AlarmMonitor);
        Assert.Equal(201, (int)ApiEmergencyStopReason.DiamondCoordCheck);
        Assert.Equal(301, (int)ApiEmergencyStopReason.UserButton);
        Assert.Equal(401, (int)ApiEmergencyStopReason.RpcService);

        Assert.Equal(0, (int)ApiEmergencyResumeReason.Unknown);
        Assert.Equal(101, (int)ApiEmergencyResumeReason.AlarmMonitorResume);
        Assert.Equal(201, (int)ApiEmergencyResumeReason.AlarmMonitorStatusChange);
        Assert.Equal(301, (int)ApiEmergencyResumeReason.UserButton);
        Assert.Equal(401, (int)ApiEmergencyResumeReason.RpcService);
    }

}
