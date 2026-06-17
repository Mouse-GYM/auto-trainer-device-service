namespace AutoTrainer.Api.Data;

public interface IAnimalDbContextFactory
{
    AnimalDbContext Create(string identifier);
}

public class AnimalDbContextFactory(ISqliteStorage storage) : IAnimalDbContextFactory
{
    public AnimalDbContext Create(string identifier)
    {
        var options = new DbContextOptionsBuilder<AnimalDbContext>()
            .UseSqlite(storage.GetAnimalConnectionString(identifier))
            .Options;
        return new AnimalDbContext(options);
    }
}
