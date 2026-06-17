namespace AutoTrainer.Api.Data.Entities;

public class ReachEventTotals : SoftDeleteEntity
{
    public DateTime Start { get; set; }
    public DateTime Stop { get; set; }
    public int Kind { get; set; }
    public int EventCount { get; set; }
    public int ReachCount { get; set; }
    public int SuccessfulReachCount { get; set; }
    public int EatenCount { get; set; }
    public int MissedCount { get; set; }
    public int DroppedCount { get; set; }
}
