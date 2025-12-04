using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Models;

public class TunnelDevice
{
    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double MagnetIntensity { get; set; }

    public bool GateOpen { get; set; }

    public void ApplyStatus(ApiTunnelStatus status)
    {
        MagnetIntensity = status.MagnetIntensity;
        GateOpen = status.GateOpen;
    }
}
