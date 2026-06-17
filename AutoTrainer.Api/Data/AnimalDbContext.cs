using AutoTrainer.Api.Data.Entities;

namespace AutoTrainer.Api.Data;

public class AnimalDbContext : AppDbContext
{
    public AnimalDbContext(DbContextOptions<AnimalDbContext> options) : base(options) { }

    public DbSet<AnimalInfo> AnimalInfo => Set<AnimalInfo>();
    public DbSet<SessionHistory> SessionHistory => Set<SessionHistory>();
    public DbSet<PelletHistory> PelletHistory => Set<PelletHistory>();
    public DbSet<PelletTotals> PelletTotals => Set<PelletTotals>();
    public DbSet<ReachEventHistory> ReachEventHistory => Set<ReachEventHistory>();
    public DbSet<ReachEventTotals> ReachEventTotals => Set<ReachEventTotals>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnimalInfo>().ToTable("AnimalInfo");
        modelBuilder.Entity<SessionHistory>().ToTable("SessionHistory");
        modelBuilder.Entity<PelletHistory>().ToTable("PelletHistory");
        modelBuilder.Entity<PelletTotals>().ToTable("PelletTotals");
        modelBuilder.Entity<ReachEventHistory>().ToTable("ReachEventHistory");
        modelBuilder.Entity<ReachEventTotals>().ToTable("ReachEventTotals");
        base.OnModelCreating(modelBuilder); // applies soft-delete filters last
    }
}
