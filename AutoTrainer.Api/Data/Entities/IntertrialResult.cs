namespace AutoTrainer.Api.Data.Entities;

// The intertrial analysis of one trial, from ApiEventKind.IntertrialResponse. TrialId is its only link
// upward: the session is reached through the trial, and so is the batch (via the trial's optional
// BatchAnalysis) -- the rule RawReachEvent's own comment states.
//
// One-to-one with Trial, so TrialId carries a UNIQUE index. A plain delete soft-deletes, like every other
// table here; a result superseded by a new one for the same trial is HARD deleted first, because the unique
// index counts soft-deleted rows and a tombstone would block the replacement. See
// AnimalDataStore.ReplaceIntertrialResultAsync.
public class IntertrialResult : SoftDeleteEntity
{
    public int TrialId { get; set; }

    [JsonIgnore]
    public Trial? Trial { get; set; }

    // Right-hand max-reach vantage points as stored JSON -- an array whose elements are [x, y, z] or null.
    // Opaque and deliberately not queryable, like Trial.IntertrialPelletShift; deserialize it to display it.
    public string? RhMaxVpList { get; set; }

    public int FoodConsumed { get; set; }
    public int SuccessfulReaches { get; set; }
    public int TotalReaches { get; set; }

    public ICollection<RawReachEvent> RawReachEvents { get; set; } = [];
    public ICollection<HandReachEvent> HandReachEvents { get; set; } = [];
    public ICollection<OtherReachEvent> OtherReachEvents { get; set; } = [];
}
