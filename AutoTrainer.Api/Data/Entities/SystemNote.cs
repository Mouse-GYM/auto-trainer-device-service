namespace AutoTrainer.Api.Data.Entities;

// One device-level system note. No owner column: the device is a singleton, so the table is the log.
public class SystemNote : SoftDeleteEntity
{
    public string Body { get; set; } = "";

    // Reserved for attribution; written null for every entry today -- the service has no identity.
    public string? AuthorId { get; set; }
}
