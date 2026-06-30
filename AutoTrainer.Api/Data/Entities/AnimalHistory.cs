namespace AutoTrainer.Api.Data.Entities;

// Append-only history: a new row per animalCreated/animalUpdated.
public class AnimalHistory : SoftDeleteEntity
{
    public string Identifier { get; set; } = "";
    public string Name { get; set; } = "";
    public double DcsSendX { get; set; }
    public double DcsSendY { get; set; }
    public double DcsSendZ { get; set; }
    public double? TargetYLimit { get; set; }
}
