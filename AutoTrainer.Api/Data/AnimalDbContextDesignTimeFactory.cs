using Microsoft.EntityFrameworkCore.Design;

namespace AutoTrainer.Api.Data;

public class AnimalDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AnimalDbContext>
{
    public AnimalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AnimalDbContext>()
            .UseSqlite("Data Source=animal-design.db")
            .Options;
        return new AnimalDbContext(options);
    }
}
