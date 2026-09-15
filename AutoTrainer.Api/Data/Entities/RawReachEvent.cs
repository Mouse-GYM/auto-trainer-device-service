namespace AutoTrainer.Api.Data.Entities;

// A single reach within a trial exactly as the analysis reported it, from IntertrialResponse.ReachEvents.
// "Raw" because an unqualified "reach" now means a right-hand HandReachEvent: these rows are the unfiltered
// source, kept for callers that specifically ask for them via /raw-reaches or the live intertrialResponse.
//
// Unlike its two siblings this keeps a direct TrialId as well as the IntertrialResultId it inherits: the
// trial-scoped count (Trial.RawReachEvents) and ReachEventDto.TrialId hang off it. The session is reached
// through the trial, and so is the batch (via the trial's optional BatchAnalysis).
public class RawReachEvent : ReachEventBase
{
    public int TrialId { get; set; }

    // Rows from this table are returned straight from GET /raw-reaches. The navigation is never loaded there
    // (no Include), so it would serialize as a permanently-null "trial" field -- and if anyone ever did
    // Include it, Trial -> RawReachEvents -> Trial is a cycle that System.Text.Json throws on. TrialId above
    // is what a caller needs; keep the navigation off the wire.
    [JsonIgnore]
    public Trial? Trial { get; set; }
}
