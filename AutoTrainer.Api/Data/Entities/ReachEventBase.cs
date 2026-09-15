namespace AutoTrainer.Api.Data.Entities;

// Shared columns for the three reach tables (ReachEvent, HandReachEvent, OtherReachEvent). Abstract and
// deliberately UNMAPPED -- no DbSet and no navigation is typed as this, so EF folds these columns into each
// concrete entity instead of building a TPH hierarchy with a discriminator. Same arrangement as
// SoftDeleteEntity above it.
//
// The three tables being column-identical is convenient, not a contract. Because each maps to its own table,
// moving a property down into one derived class is a C# edit that changes no schema -- so if one of them
// needs to diverge, take it out of here rather than widening this for all three.
public abstract class ReachEventBase : SoftDeleteEntity
{
    public int IntertrialResultId { get; set; }

    // IntertrialResult -> children -> IntertrialResult is a cycle System.Text.Json throws on; the id above is
    // what a caller needs.
    [JsonIgnore]
    public IntertrialResult? IntertrialResult { get; set; }

    // Method/Outcome stored as integer codes; see ReachEventMethod.ToCode / ReachEventOutcome.ToCode.
    public int Method { get; set; }
    public int Outcome { get; set; }
    public int FirstFrame { get; set; }
    public int? LastFrame { get; set; }
    public int? MaxFrame { get; set; }
    public double DelaySincePresented { get; set; }
}
