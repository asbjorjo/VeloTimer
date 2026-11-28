using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Storage;

public class TimingDbContext : BaseDbContext
{
    public TimingDbContext(DbContextOptions<TimingDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("timing");

        modelBuilder.Entity<Passing>(e =>
        {
            e.HasOne(p => p.TimingPoint)
                .WithMany()
                .IsRequired()
                .OnDelete(DeleteBehavior.ClientNoAction);
            e.HasOne(p => p.Transponder)
                .WithMany()
                .IsRequired()
                .OnDelete(DeleteBehavior.ClientNoAction);
            e.HasOne(p => p.Installation)
                .WithMany()
                .IsRequired()
                .OnDelete(DeleteBehavior.ClientNoAction);
            // Observation identity: an agent's passing ID is unique within its installation.
            e.HasIndex(p => new { p.InstallationId, p.SourcePassingId }).IsUnique();
            // One recorded fact per transponder, timing point and instant.
            e.HasIndex(p => new {p.TransponderId, p.Time, p.TimingPointId}).IsUnique();
            // Adjacent-passing reads: previous passing per installation and transponder, ordered by (Time, Id).
            e.HasIndex(p => new { p.InstallationId, p.TransponderId, p.Time, p.Id });
            e.HasIndex(p => p.Time);
        });

        modelBuilder.Entity<PassingConflict>(e =>
        {
            e.HasIndex(c => new
            {
                c.InstallationId, c.SourcePassingId, c.Reason, c.TransponderId, c.TimingPointId, c.Time,
                c.LowBattery, c.LowStrength, c.LowHits
            }).IsUnique();
            e.HasIndex(c => c.LastSeenAt);
        });

        modelBuilder.Entity<RejectedPassing>(e =>
        {
            e.HasIndex(r => new { r.AgentId, r.SourcePassingId }).IsUnique();
            e.HasIndex(r => r.LastSeenAt);
        });

        modelBuilder.Entity<TimingPoint>(e =>
        {
            e.HasIndex(tp => new { tp.InstallationId, tp.SystemId }).IsUnique();
        });

        modelBuilder.Entity<Transponder>(e =>
        {
            e.Property(transponder => transponder.Label).UsePropertyAccessMode(PropertyAccessMode.Property);
            e.HasDiscriminator(t => t.Type)
                .HasValue<MylapsProChipTransponder>(TransponderType.MylapsProChip);
            e.HasIndex(t => new { t.Type, t.SystemId }).IsUnique();
        });

        modelBuilder.Entity<Installation>(e =>
        {
            var capabilities = e.Property(i => i.AgentCapabilities)
                .HasConversion(AgentCapabilityJson.Converter, AgentCapabilityJson.Comparer)
                .IsRequired();
            if (Database.IsNpgsql()) capabilities.HasColumnType("jsonb");
            e.HasMany(i => i.TimingPoints)
                .WithOne(tp => tp.Installation)
                .IsRequired();
            e.HasMany(i => i.FacilityAssignments)
                .WithOne(a => a.Installation)
                .HasForeignKey(a => a.InstallationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<InstallationFacilityAssignment>(e =>
        {
            e.HasIndex(a => new { a.InstallationId, a.ValidFromUtc }).IsUnique();
            e.HasIndex(a => a.FacilityId);
            if (Database.IsNpgsql())
            {
                e.ToTable(t => t.HasCheckConstraint("ck_installation_facility_assignment_interval",
                    "valid_from_utc IS NULL OR valid_to_utc IS NULL OR valid_from_utc < valid_to_utc"));
            }
        });
        modelBuilder.Entity<TransponderOwner>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasOne(t => t.Transponder)
                .WithMany(t => t.Owners)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(t => t.OwnedFrom)
                .IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}

public class TimingDbContextFactory : IDesignTimeDbContextFactory<TimingDbContext>
{
    public TimingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TimingDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;port=25432;Database=velotime;Username=velotime;Password=velotime",
            options =>
            {
                options.MigrationsHistoryTable("__ef_migrations_history", "ef");
            });
        optionsBuilder.UseSnakeCaseNamingConvention();
        return new TimingDbContext(optionsBuilder.Options);
    }
}
