namespace AutoTrainer.Api.Data.Entities;

public interface IAuditable
{
    int Id { get; set; }
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}

public interface ISoftDelete : IAuditable
{
    DateTime? DeletedAt { get; set; }
}
