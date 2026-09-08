using System.Text.Json.Serialization;

namespace AutoTrainer.Api.ApiTypes;

public readonly record struct ApiPelletStatus
{
    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsSendX { get; init; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsSendY { get; init; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsSendZ { get; init; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsX { get; init; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsY { get; init; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsZ { get; init; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double LoadArm { get; init; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double BarrierArm { get; init; }

    public bool IsBarrierArmActive { get; init; }

    // Settings, each set by the like-named ApiCommandKind in the 1000-1099 range.
    public bool IsHomeOnExcessiveDriftEnabled { get; init; }

    public bool IsTunnelSweepEnabled { get; init; }
}
