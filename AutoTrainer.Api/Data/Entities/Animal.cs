namespace AutoTrainer.Api.Data.Entities;

// Device-level registry of every animal seen: one row per animal identifier, created the first time that
// animal's per-animal database is created. Distinct from AnimalHistory, which is the append-only identity/DCS
// record kept inside each animal's own database.
public class Animal : SoftDeleteEntity
{
    public string Identifier { get; set; } = "";
    public string Name { get; set; } = "";
    public string TrainerNotes { get; set; } = "";
}
