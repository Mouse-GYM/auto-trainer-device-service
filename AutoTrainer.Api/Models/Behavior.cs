using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Models;

public class Behavior
{
    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double BaselineMagnetIntensity { get; set; }

    // Not part of status - is very transient.  Indicates load cell active in the context of the
    // behavior algorithm, not just raw sensor analysis.
    public bool LoadCellEnabled { get; set; }

    public void ApplyStatus(ApiBehaviorStatus status)
    {
        BaselineMagnetIntensity = status.BaselineMagnetIntensity;
    }
}
