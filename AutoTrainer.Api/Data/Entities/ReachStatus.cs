namespace AutoTrainer.Api.Data.Entities;

// Snapshots of ApiReachStatus taken from systemStatus messages, appended only when the value changes.
//
// The four counts are deliberately duplicated rather than pulled into a shared base class: a mapped base
// would make EF build a TPH hierarchy (one table plus a Discriminator column) instead of two tables.

public class ReachStatusTotal : SoftDeleteEntity
{
    public int PelletsPresented { get; set; }
    public int PelletsConsumed { get; set; }
    public int Reaches { get; set; }
    public int SuccessfulReaches { get; set; }
}

public class ReachStatusDay : SoftDeleteEntity
{
    public int PelletsPresented { get; set; }
    public int PelletsConsumed { get; set; }
    public int Reaches { get; set; }
    public int SuccessfulReaches { get; set; }

    // The day these counts belong to, parsed from the final component of ApiProjectStatus.DayPath.
    // Without it, a rollover to zeros is indistinguishable from a genuine zero row.
    public DateOnly? Day { get; set; }
}
