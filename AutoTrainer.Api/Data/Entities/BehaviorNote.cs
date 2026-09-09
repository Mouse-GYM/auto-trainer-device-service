namespace AutoTrainer.Api.Data.Entities;

// One behavior note for one registry animal. No navigation property in either direction: every query is
// Where(n => n.AnimalId == ...), and a required navigation into the soft-delete-filtered Animal is what would
// trip EF's model-validation warning.
public class BehaviorNote : SoftDeleteEntity
{
    public int AnimalId { get; set; }

    public string Body { get; set; } = "";

    // Reserved for attribution; written null for every entry today -- the service has no identity.
    public string? AuthorId { get; set; }
}
