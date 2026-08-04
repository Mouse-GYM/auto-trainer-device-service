using AutoTrainer.Api.Models;
using Xunit;

namespace AutoTrainer.Api.Tests.Models;

// The producer strings are documented in agents/emergency-stop-reasons.md and
// agents/emergency-resume-reasons.md, and are exhaustive for non-test producer code.
public class EmergencyReasonTests
{
    [Theory]
    [InlineData("user-button", EmergencyStopReason.UserButton)]
    [InlineData("RpcService", EmergencyStopReason.RpcService)]
    [InlineData("Diamond-Coord-Check", EmergencyStopReason.DiamondCoordCheck)]
    // Every alarm-monitor variant collapses to one member: the suffix is variable and unordered, and the
    // payload's active_alarms already carries that information structurally.
    [InlineData("alarm-monitor: DOORS_OPEN", EmergencyStopReason.AlarmMonitor)]
    [InlineData("alarm-monitor: DOORS_OPEN SYSTEM_FAULT MOUSE_THRASHING", EmergencyStopReason.AlarmMonitor)]
    [InlineData("alarm-monitor: ", EmergencyStopReason.AlarmMonitor)]
    [InlineData("alarm-monitor:", EmergencyStopReason.AlarmMonitor)]
    public void StopReason_MapsDocumentedStrings(string reason, EmergencyStopReason expected) =>
        Assert.Equal(expected, EmergencyStopReasons.FromReason(reason));

    [Theory]
    [InlineData("alarm-monitor-resumed", EmergencyResumeReason.AlarmMonitorResumed)]
    [InlineData("alarm-monitor-no-valid-condition-remaining",
        EmergencyResumeReason.AlarmMonitorNoValidConditionRemaining)]
    [InlineData("user-button", EmergencyResumeReason.UserButton)]
    [InlineData("RpcService", EmergencyResumeReason.RpcService)]
    public void ResumeReason_MapsDocumentedStrings(string reason, EmergencyResumeReason expected) =>
        Assert.Equal(expected, EmergencyResumeReasons.FromReason(reason));

    // The whole point of two separate converters. The stop form carries a colon; the two resume literals
    // share the "alarm-monitor" prefix without one, so a bare-prefix test would cross-contaminate.
    [Theory]
    [InlineData("alarm-monitor-resumed")]
    [InlineData("alarm-monitor-no-valid-condition-remaining")]
    public void StopReason_DoesNotMatchResumeAlarmMonitorLiterals(string resumeReason) =>
        Assert.Equal(EmergencyStopReason.Unknown, EmergencyStopReasons.FromReason(resumeReason));

    [Theory]
    [InlineData("alarm-monitor: DOORS_OPEN")]
    [InlineData("Diamond-Coord-Check")]
    public void ResumeReason_DoesNotMatchStopOnlyReasons(string stopReason) =>
        Assert.Equal(EmergencyResumeReason.Unknown, EmergencyResumeReasons.FromReason(stopReason));

    // RpcService and Diamond-Coord-Check are mixed-case producer literals; matching must stay ordinal.
    [Theory]
    [InlineData("rpcservice")]
    [InlineData("RPCSERVICE")]
    [InlineData("USER-BUTTON")]
    [InlineData("diamond-coord-check")]
    [InlineData("Alarm-Monitor: DOORS_OPEN")]
    public void StopReason_IsCaseSensitive(string reason) =>
        Assert.Equal(EmergencyStopReason.Unknown, EmergencyStopReasons.FromReason(reason));

    [Theory]
    [InlineData("rpcservice")]
    [InlineData("USER-BUTTON")]
    [InlineData("ALARM-MONITOR-RESUMED")]
    public void ResumeReason_IsCaseSensitive(string reason) =>
        Assert.Equal(EmergencyResumeReason.Unknown, EmergencyResumeReasons.FromReason(reason));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something-the-producer-added-later")]
    public void UnrecognizedStrings_MapToUnknown(string? reason)
    {
        Assert.Equal(EmergencyStopReason.Unknown, EmergencyStopReasons.FromReason(reason));
        Assert.Equal(EmergencyResumeReason.Unknown, EmergencyResumeReasons.FromReason(reason));
    }

    // These values are the ones the Python producer is expected to inherit, so drift is a cross-repo
    // breaking change, not a local refactor.
    [Fact]
    public void EnumValues_AreTheAgreedNumbering()
    {
        Assert.Equal(0, (int)EmergencyStopReason.Unknown);
        Assert.Equal(101, (int)EmergencyStopReason.AlarmMonitor);
        Assert.Equal(201, (int)EmergencyStopReason.UserButton);
        Assert.Equal(301, (int)EmergencyStopReason.RpcService);
        Assert.Equal(401, (int)EmergencyStopReason.DiamondCoordCheck);

        Assert.Equal(0, (int)EmergencyResumeReason.Unknown);
        Assert.Equal(101, (int)EmergencyResumeReason.AlarmMonitorResumed);
        Assert.Equal(102, (int)EmergencyResumeReason.AlarmMonitorNoValidConditionRemaining);
        Assert.Equal(201, (int)EmergencyResumeReason.UserButton);
        Assert.Equal(301, (int)EmergencyResumeReason.RpcService);
    }
}
