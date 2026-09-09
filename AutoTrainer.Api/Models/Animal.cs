using AutoTrainer.Api.ApiTypes;
using AutoTrainer.Api.Contracts;

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

    // Rolling total of the last 5 calendar days from the ReachStatusDay table. Computed server-side (never in a
    // zeromq message), so it is populated/maintained outside ApplyStatus and left untouched here.
    public ReachStatus ReachStatus5Day { get; } = new();

    // Service-owned free text from the device registry (never in a zeromq message). Stamped in
    // StampAndBroadcastAnimal like ReachStatus5Day, so ApplyStatus must leave both untouched.
    public string TrainerNotes { get; set; } = "";

    // The newest entry in this animal's behavior-note log; null when the log is empty.
    public NoteDto? BehaviorNote { get; set; }

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
