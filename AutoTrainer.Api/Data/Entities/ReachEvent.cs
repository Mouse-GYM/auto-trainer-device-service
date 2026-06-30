namespace AutoTrainer.Api.Data.Entities;

// A single reach within a trial. The trial is its ONLY relationship: the session is reached through the
// trial, and so is the batch (via the trial's optional BatchAnalysis).
public class ReachEvent : SoftDeleteEntity
{
    public int TrialId { get; set; }

    // Rows from this table are returned straight from GET /device/reaches. The navigation is never loaded
    // there (no Include), so it would serialize as a permanently-null "trial" field -- and if anyone ever did
    // Include it, Trial -> ReachEvents -> Trial is a cycle that System.Text.Json throws on. TrialId above is
    // what a caller needs; keep the navigation off the wire.
    [JsonIgnore]
    public Trial? Trial { get; set; }

    // Method/Outcome stored as integer codes; see ReachEventMethod.ToCode / ReachEventOutcome.ToCode.
    public int Method { get; set; }
    public int Outcome { get; set; }
    public int FirstFrame { get; set; }
    public int? LastFrame { get; set; }
    public int? MaxFrame { get; set; }
    public double DelaySincePresented { get; set; }
}
