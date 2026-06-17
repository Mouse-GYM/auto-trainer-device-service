namespace AutoTrainer.Api.Data.Entities;

public class PelletHistory : SoftDeleteEntity
{
    public int Action { get; set; }
    public int Count { get; set; }
}
