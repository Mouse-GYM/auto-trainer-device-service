using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Models;

public class TunnelDevice
{
    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double MagnetIntensity { get; set; }

    public bool IsGateOpen { get; set; }

    public bool IsFanOn { get; set; }

    public void ApplyStatus(ApiTunnelStatus status)
    {
        MagnetIntensity = status.MagnetIntensity;
        IsGateOpen = status.IsGateOpen;
        IsFanOn = status.IsFanOn;
    }
}
