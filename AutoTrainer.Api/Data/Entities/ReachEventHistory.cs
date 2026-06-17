namespace AutoTrainer.Api.Data.Entities;

public class ReachEventHistory : SoftDeleteEntity
{
    // Method/Outcome stored as integer codes; see ReachEventMethod.ToCode / ReachEventOutcome.ToCode.
    public int Method { get; set; }
    public int Outcome { get; set; }
    public int FirstFrame { get; set; }
    public int? LastFrame { get; set; }
    public int? MaxFrame { get; set; }
    public double DelaySincePresented { get; set; }
}
