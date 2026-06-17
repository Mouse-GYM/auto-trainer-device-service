using AutoTrainer.Api.Data.Entities;

namespace AutoTrainer.Api.Data;

public class DeviceDbContext : AppDbContext
{
    public DeviceDbContext(DbContextOptions<DeviceDbContext> options) : base(options) { }

    public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();
    public DbSet<DetectorHistory> DetectorHistory => Set<DetectorHistory>();
    public DbSet<AlarmHistory> AlarmHistory => Set<AlarmHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemConfiguration>().ToTable("SystemConfiguration");
        modelBuilder.Entity<DetectorHistory>().ToTable("DetectorHistory");
        modelBuilder.Entity<AlarmHistory>().ToTable("AlarmHistory");
        base.OnModelCreating(modelBuilder); // applies soft-delete filters last
    }
}
