namespace AutoTrainer.Api.Data.Entities;

public class PelletTotals : SoftDeleteEntity
{
    public DateTime Start { get; set; }
    public DateTime Stop { get; set; }
    public int Kind { get; set; }
    public int Presented { get; set; }
    public int Consumed { get; set; }
    public int? Other { get; set; }
}
