namespace AutoTrainer.Api.Data.Entities;

// Hard-delete base: audit timestamps only. Reserved for future tables.
public abstract class AuditableEntity : IAuditable
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// Soft-delete base (default for all initial tables).
public abstract class SoftDeleteEntity : AuditableEntity, ISoftDelete
{
    public DateTime? DeletedAt { get; set; }
}
