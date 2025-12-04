using AutoTrainer.Api.ApiTypes;

namespace AutoTrainer.Api.Models;

public class ReachStatus
{
    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double PelletsPresented { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double PelletsConsumed { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double Reaches { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double SuccessfulReaches { get; set; }

    public void ApplyStatus(ApiReachStatus status)
    {
        PelletsPresented = status.PelletsPresented;
        PelletsConsumed = status.PelletsConsumed;
        Reaches = status.Reaches;
        SuccessfulReaches = status.SuccessfulReaches;
    }
}

public class Animal
{
    public string Identifier { get; set; } = "";

    public string Name { get; set; } = "";

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsSendX { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsSendY { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double DcsSendZ { get; set; }

    [JsonConverter(typeof(NullNanDoubleConverter))]
    public double TargetYLimit { get; set; } = double.NaN;

    public ReachStatus ReachStatusTotal { get; } = new();

    public ReachStatus ReachStatusDay { get; } = new();

    public void ApplyStatus(ApiAnimalStatus status)
    {
        Identifier = status.Identifier;
        Name = status.Name;
        DcsSendX = status.DcsSendX;
        DcsSendY = status.DcsSendY;
        DcsSendZ = status.DcsSendZ;
        TargetYLimit = status.TargetYLimit ?? double.NaN;
        ReachStatusTotal.ApplyStatus(status.ReachStatusTotal);
        ReachStatusDay.ApplyStatus(status.ReachStatusDay);
    }
}
