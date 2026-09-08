using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Models;

public class PelletDevice
{
    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsSendX { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsSendY { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsSendZ { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsX { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsY { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsZ { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double LoadArm { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double BarrierArm { get; set; }

    public bool IsBarrierArmActive { get; set; }

    // Behavior settings the producer reports on the pellet device rather than on ApiBehaviorStatus, each set by
    // the like-named ApiCommandKind in the 1000-1099 range.
    public bool IsHomeOnExcessiveDriftEnabled { get; set; }

    public bool IsTunnelSweepEnabled { get; set; }

    public ApiEventKind LastCommand { get; set; } = ApiEventKind.PropertyChanged;

    public double LastCommandWhen { get; set; }

    public void UpdateLastCommand(ApiEvent apiEvent)
    {
        LastCommand = apiEvent.Kind;
        LastCommandWhen = apiEvent.When;
    }

    public void ApplyStatus(ApiPelletStatus status)
    {
        DcsSendX = status.DcsSendX;
        DcsSendY = status.DcsSendY;
        DcsSendZ = status.DcsSendZ;

        DcsX = status.DcsX;
        DcsY = status.DcsY;
        DcsZ = status.DcsZ;

        LoadArm = status.LoadArm;
        BarrierArm = status.BarrierArm;

        IsBarrierArmActive = status.IsBarrierArmActive;

        IsHomeOnExcessiveDriftEnabled = status.IsHomeOnExcessiveDriftEnabled;
        IsTunnelSweepEnabled = status.IsTunnelSweepEnabled;
    }
}
