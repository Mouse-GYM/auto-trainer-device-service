using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Models;
using Xunit;

namespace AutoTrainer.Api.Tests.Models;

// The ApplyStatus mappings from the API status types onto the retained models. These are long runs of
// one-line assignments, which is exactly where an omitted or transposed field hides: every value is the same
// type, so the compiler cannot help and no other test reads them.
//
// Each bool field gets an alternating value rather than all-true, so a transposition between two neighbours
// is a failure rather than a coincidence.
public class StatusMappingTests
{
    [Fact]
    public void Behavior_ApplyStatus_MapsEveryFieldToItsOwnProperty()
    {
        var behavior = new Behavior();

        behavior.ApplyStatus(new ApiBehaviorStatus
        {
            BaselineMagnetIntensity = 12.5,
            IsLiveAnalysisEnabled = true,
            IsPelletDeliveryEnabled = false,
            IsPelletCoverEnabled = true,
            IsIntertrialPelletShiftEnabled = false,
            IsTrianglePelletDistanceDetectionEnabled = true,
            IsAutoCloseGateOnIntertrialEnabled = false,
            IsAutoClampEnabled = true,
            IsBatchTrialsEnabled = false
        });

        Assert.Equal(12.5, behavior.BaselineMagnetIntensity);
        Assert.True(behavior.IsLiveAnalysisEnabled);
        Assert.False(behavior.IsPelletDeliveryEnabled);
        Assert.True(behavior.IsPelletCoverEnabled);
        Assert.False(behavior.IsIntertrialPelletShiftEnabled);
        Assert.True(behavior.IsTrianglePelletDistanceDetectionEnabled);
        Assert.False(behavior.IsAutoCloseGateOnIntertrialEnabled);
        Assert.True(behavior.IsAutoClampEnabled);
        Assert.False(behavior.IsBatchTrialsEnabled);
    }

    // The inverse pattern. Run alone, the case above passes against a mapping that assigns a constant or
    // swaps a pair whose values happen to match; together the two pin each field independently.
    [Fact]
    public void Behavior_ApplyStatus_MapsTheInvertedPattern()
    {
        var behavior = new Behavior();

        behavior.ApplyStatus(new ApiBehaviorStatus
        {
            IsLiveAnalysisEnabled = false,
            IsPelletDeliveryEnabled = true,
            IsPelletCoverEnabled = false,
            IsIntertrialPelletShiftEnabled = true,
            IsTrianglePelletDistanceDetectionEnabled = false,
            IsAutoCloseGateOnIntertrialEnabled = true,
            IsAutoClampEnabled = false,
            IsBatchTrialsEnabled = true
        });

        Assert.False(behavior.IsLiveAnalysisEnabled);
        Assert.True(behavior.IsPelletDeliveryEnabled);
        Assert.False(behavior.IsPelletCoverEnabled);
        Assert.True(behavior.IsIntertrialPelletShiftEnabled);
        Assert.False(behavior.IsTrianglePelletDistanceDetectionEnabled);
        Assert.True(behavior.IsAutoCloseGateOnIntertrialEnabled);
        Assert.False(behavior.IsAutoClampEnabled);
        Assert.True(behavior.IsBatchTrialsEnabled);
    }

    // ApplyStatus is called repeatedly on the retained instance, so a field that is only ever set when true
    // would leave a stale true here.
    [Fact]
    public void Behavior_ApplyStatus_ClearsWhatAPreviousStatusSet()
    {
        var behavior = new Behavior();

        behavior.ApplyStatus(new ApiBehaviorStatus
        {
            IsLiveAnalysisEnabled = true,
            IsPelletDeliveryEnabled = true,
            IsPelletCoverEnabled = true,
            IsIntertrialPelletShiftEnabled = true,
            IsTrianglePelletDistanceDetectionEnabled = true,
            IsAutoCloseGateOnIntertrialEnabled = true,
            IsAutoClampEnabled = true,
            IsBatchTrialsEnabled = true
        });

        behavior.ApplyStatus(new ApiBehaviorStatus());

        Assert.False(behavior.IsLiveAnalysisEnabled);
        Assert.False(behavior.IsPelletDeliveryEnabled);
        Assert.False(behavior.IsPelletCoverEnabled);
        Assert.False(behavior.IsIntertrialPelletShiftEnabled);
        Assert.False(behavior.IsTrianglePelletDistanceDetectionEnabled);
        Assert.False(behavior.IsAutoCloseGateOnIntertrialEnabled);
        Assert.False(behavior.IsAutoClampEnabled);
        Assert.False(behavior.IsBatchTrialsEnabled);
    }

    [Fact]
    public void PelletDevice_ApplyStatus_MapsEveryFieldToItsOwnProperty()
    {
        var pellet = new PelletDevice();

        pellet.ApplyStatus(new ApiPelletStatus
        {
            DcsSendX = 1, DcsSendY = 2, DcsSendZ = 3,
            DcsX = 4, DcsY = 5, DcsZ = 6,
            LoadArm = 7, BarrierArm = 8,
            IsBarrierArmActive = true,
            IsHomeOnExcessiveDriftEnabled = false,
            IsTunnelSweepEnabled = true
        });

        Assert.Equal(1, pellet.DcsSendX);
        Assert.Equal(2, pellet.DcsSendY);
        Assert.Equal(3, pellet.DcsSendZ);
        Assert.Equal(4, pellet.DcsX);
        Assert.Equal(5, pellet.DcsY);
        Assert.Equal(6, pellet.DcsZ);
        Assert.Equal(7, pellet.LoadArm);
        Assert.Equal(8, pellet.BarrierArm);
        Assert.True(pellet.IsBarrierArmActive);
        Assert.False(pellet.IsHomeOnExcessiveDriftEnabled);
        Assert.True(pellet.IsTunnelSweepEnabled);
    }

    [Fact]
    public void PelletDevice_ApplyStatus_MapsTheInvertedPattern()
    {
        var pellet = new PelletDevice();

        pellet.ApplyStatus(new ApiPelletStatus
        {
            IsBarrierArmActive = false,
            IsHomeOnExcessiveDriftEnabled = true,
            IsTunnelSweepEnabled = false
        });

        Assert.False(pellet.IsBarrierArmActive);
        Assert.True(pellet.IsHomeOnExcessiveDriftEnabled);
        Assert.False(pellet.IsTunnelSweepEnabled);
    }

    [Fact]
    public void TunnelDevice_ApplyStatus_MapsEveryFieldToItsOwnProperty()
    {
        var tunnel = new TunnelDevice();

        tunnel.ApplyStatus(new ApiTunnelStatus
        {
            MagnetIntensity = 9.5,
            IsGateOpen = true,
            IsFanOn = false
        });

        Assert.Equal(9.5, tunnel.MagnetIntensity);
        Assert.True(tunnel.IsGateOpen);
        Assert.False(tunnel.IsFanOn);
    }

    [Fact]
    public void TunnelDevice_ApplyStatus_MapsTheInvertedPattern()
    {
        var tunnel = new TunnelDevice();

        tunnel.ApplyStatus(new ApiTunnelStatus { IsGateOpen = false, IsFanOn = true });

        Assert.False(tunnel.IsGateOpen);
        Assert.True(tunnel.IsFanOn);
    }
}
