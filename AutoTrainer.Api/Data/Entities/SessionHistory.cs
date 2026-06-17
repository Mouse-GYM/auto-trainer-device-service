namespace AutoTrainer.Api.Data.Entities;

public class SessionHistory : SoftDeleteEntity
{
    public DateTime Enter { get; set; }
    public DateTime Exit { get; set; }
    public int TrialCount { get; set; }
}
