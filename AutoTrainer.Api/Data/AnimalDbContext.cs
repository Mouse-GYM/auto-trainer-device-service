using AutoTrainer.Api.Data.Entities;

namespace AutoTrainer.Api.Data;

public class AnimalDbContext : AppDbContext
{
    public AnimalDbContext(DbContextOptions<AnimalDbContext> options) : base(options) { }

    public DbSet<AnimalHistory> AnimalHistory => Set<AnimalHistory>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Trial> Trials => Set<Trial>();
    public DbSet<BatchAnalysis> BatchAnalyses => Set<BatchAnalysis>();
    public DbSet<ReachEvent> ReachEvents => Set<ReachEvent>();
    public DbSet<ReachStatusTotal> ReachStatusTotals => Set<ReachStatusTotal>();
    public DbSet<ReachStatusDay> ReachStatusDays => Set<ReachStatusDay>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnimalHistory>().ToTable("AnimalHistory");

        modelBuilder.Entity<Session>(e =>
        {
            e.ToTable("Session");
            e.Property(x => x.Identifier).IsRequired();
            e.HasIndex(x => x.Identifier).IsUnique();
        });

        modelBuilder.Entity<BatchAnalysis>(e =>
        {
            e.ToTable("BatchAnalysis");
            e.Property(x => x.Identifier).IsRequired();
            e.HasIndex(x => x.Identifier).IsUnique();
            e.HasOne(x => x.Session).WithMany(s => s.BatchAnalyses)
                .HasForeignKey(x => x.SessionId).IsRequired();
        });

        modelBuilder.Entity<Trial>(e =>
        {
            e.ToTable("Trial");
            // trial_id is unique only within a session, so this composite is the trial's key.
            e.HasIndex(x => new { x.SessionId, x.Identifier }).IsUnique();
            e.HasOne(x => x.Session).WithMany(s => s.Trials)
                .HasForeignKey(x => x.SessionId).IsRequired();
            e.HasOne(x => x.BatchAnalysis).WithMany(b => b.Trials)
                .HasForeignKey(x => x.BatchAnalysisId).IsRequired(false);
        });

        modelBuilder.Entity<ReachEvent>(e =>
        {
            e.ToTable("ReachEvent");
            e.HasOne(x => x.Trial).WithMany(t => t.ReachEvents)
                .HasForeignKey(x => x.TrialId).IsRequired();
        });

        modelBuilder.Entity<ReachStatusTotal>().ToTable("ReachStatusTotal");
        modelBuilder.Entity<ReachStatusDay>().ToTable("ReachStatusDay");

        base.OnModelCreating(modelBuilder); // applies soft-delete filters last
    }
}
