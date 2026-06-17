using Microsoft.EntityFrameworkCore.Design;

namespace AutoTrainer.Api.Data;

public class DeviceDbContextDesignTimeFactory : IDesignTimeDbContextFactory<DeviceDbContext>
{
    public DeviceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DeviceDbContext>()
            .UseSqlite("Data Source=device-design.db")
            .Options;
        return new DeviceDbContext(options);
    }
}
