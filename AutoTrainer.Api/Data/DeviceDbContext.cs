using AutoTrainer.Api.Data.Entities;

namespace AutoTrainer.Api.Data;

public class DeviceDbContext : AppDbContext
{
    public DeviceDbContext(DbContextOptions<DeviceDbContext> options) : base(options) { }

    public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();
    public DbSet<DetectorHistory> DetectorHistory => Set<DetectorHistory>();
    public DbSet<AlarmHistory> AlarmHistory => Set<AlarmHistory>();
    public DbSet<EmergencyStopHistory> EmergencyStopHistory => Set<EmergencyStopHistory>();
    public DbSet<Animal> Animals => Set<Animal>();
    public DbSet<SystemNote> SystemNotes => Set<SystemNote>();
    public DbSet<BehaviorNote> BehaviorNotes => Set<BehaviorNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemConfiguration>().ToTable("SystemConfiguration");
        modelBuilder.Entity<DetectorHistory>().ToTable("DetectorHistory");
        modelBuilder.Entity<AlarmHistory>().ToTable("AlarmHistory");

        modelBuilder.Entity<EmergencyStopHistory>(e =>
        {
            e.ToTable("EmergencyStopHistory");
            e.HasIndex(x => x.OccurredAt);   // time-ordered reads
        });

        // These history tables grow without bound on a long-running device. The bare (time) index serves the
        // unfiltered read; the composite (kind, time) serves the kind-filtered one, which it cannot.
        // EventIndex is deliberately unindexed -- the notification stamp updates by primary key, so nothing
        // queries by it.
        modelBuilder.Entity<AlarmHistory>().HasIndex(x => x.CreatedAt);
        modelBuilder.Entity<AlarmHistory>().HasIndex(x => new { x.AlarmId, x.CreatedAt });
        modelBuilder.Entity<DetectorHistory>().HasIndex(x => x.CreatedAt);
        modelBuilder.Entity<DetectorHistory>().HasIndex(x => new { x.DetectorId, x.CreatedAt });

        modelBuilder.Entity<Animal>(e =>
        {
            e.ToTable("Animal");
            e.Property(x => x.Identifier).IsRequired();
            e.HasIndex(x => x.Identifier).IsUnique();
        });

        modelBuilder.Entity<SystemNote>(e =>
        {
            e.ToTable("SystemNote");
            e.Property(x => x.Body).IsRequired();
            e.HasIndex(x => x.CreatedAt);   // the log is always read in time order
        });

        modelBuilder.Entity<BehaviorNote>(e =>
        {
            e.ToTable("BehaviorNote");
            e.Property(x => x.Body).IsRequired();
            e.HasOne<Animal>().WithMany().HasForeignKey(x => x.AnimalId).IsRequired();
            // Every read is scoped to one animal, so the composite index is the only one needed.
            e.HasIndex(x => new { x.AnimalId, x.CreatedAt });
        });

        base.OnModelCreating(modelBuilder); // applies soft-delete filters last
    }
}
