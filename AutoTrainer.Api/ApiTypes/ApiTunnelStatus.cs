namespace AutoTrainer.Api.ApiTypes;

public readonly record struct ApiTunnelStatus
{
    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double MagnetIntensity { get; init; }

    public bool IsGateOpen { get; init; }

    public bool IsFanOn { get; init; }
}
