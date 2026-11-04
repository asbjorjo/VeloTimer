using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Storage;

public class StatisticsDbContext : BaseDbContext
{
    public StatisticsDbContext(DbContextOptions<StatisticsDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("statistics");
        if (Database.IsNpgsql())
        {
            // PostgreSQL literal substring searches on public profile names use gin_trgm_ops.
            modelBuilder.HasPostgresExtension("public", "pg_trgm");
        }

        modelBuilder.Entity<Sample>();
        modelBuilder.Entity<StatisticsItem>(e =>
        {
            if (Database.IsNpgsql())
            {
                e.ToTable(t => t.HasCheckConstraint("ck_statistics_item_duration_bounds",
                    "(min_duration IS NULL OR min_duration > INTERVAL '0 seconds') AND " +
                    "(max_duration IS NULL OR max_duration > INTERVAL '0 seconds') AND " +
                    "(min_duration IS NULL OR max_duration IS NULL OR min_duration <= max_duration)"));
            }
        });
        modelBuilder.Entity<StatisticsItemConfig>(e =>
        {
            if (Database.IsNpgsql())
            {
                e.ToTable(t => t.HasCheckConstraint("ck_statistics_item_config_duration_bounds",
                    "(min_duration IS NULL OR min_duration > INTERVAL '0 seconds') AND " +
                    "(max_duration IS NULL OR max_duration > INTERVAL '0 seconds') AND " +
                    "(min_duration IS NULL OR max_duration IS NULL OR min_duration <= max_duration)"));
            }
        });
        modelBuilder.Entity<SimpleStatisticsItemConfig>(e =>
        {
            e.HasOne(s => s.StatisticsItem)
                .WithMany()
                .IsRequired()
                .OnDelete(DeleteBehavior.ClientNoAction);
            e.HasIndex(s => new { s.StatisticsItemId, s.CoursePointStart, s.CoursePointEnd });

        });
        modelBuilder.Entity<MultiStatisticsItemConfig>(e =>
        {
            e.HasOne(m => m.StatisticsItem)
                .WithMany()
                .IsRequired()
                .OnDelete(DeleteBehavior.ClientNoAction);
            e.HasIndex(m => new { m.StatisticsItemId, m.ParentConfigId });
        });
        modelBuilder.Entity<StatisticsEntry>(e =>
        {
            e.Property(s => s.IsValid).HasDefaultValue(true);
            if (Database.IsNpgsql())
            {
                e.ToTable(t => t.HasCheckConstraint("ck_statistics_entry_validity",
                    "(is_valid AND invalid_reason IS NULL) OR " +
                    "(NOT is_valid AND invalid_reason IS NOT NULL AND invalid_reason IN (0, 1, 2, 3))"));
            }
            e.HasIndex(s => new { s.StatisticsItemId, s.Id })
                .IsCreatedConcurrently();
            e.HasOne(e => e.StatisticsItem)
                .WithMany()
                .IsRequired()
                .OnDelete(DeleteBehavior.ClientNoAction);
            e.HasOne(e => e.StatsProfile)
                .WithMany()
                .HasForeignKey(e => e.StatsProfileUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => new { s.StatisticsItemId, s.TransponderId, s.TimeStart, s.TimeEnd });
            e.HasIndex(s => new
                {
                    s.FacilityId,
                    s.StatisticsItemId,
                    s.StatisticsItemConfigId,
                    s.TimeEnd,
                    s.Id
                })
                .IsDescending(false, false, false, true, true)
                .IsCreatedConcurrently()
                .HasDatabaseName("ix_statistics_entry_facility_item_config_time_end_id");
        });
        modelBuilder.Entity<StatisticsValidityRevalidation>(e =>
        {
            e.HasKey(r => r.StatisticsItemId);
            e.HasOne(r => r.StatisticsItem)
                .WithMany()
                .HasForeignKey(r => r.StatisticsItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(r => r.Generation).IsConcurrencyToken();
            e.HasIndex(r => new { r.IsPending, r.StatisticsItemId });
            if (Database.IsNpgsql())
            {
                e.ToTable(t => t.HasCheckConstraint("ck_statistics_validity_revalidation",
                    "generation > 0 AND phase IN (0, 1)"));
            }
        });
        modelBuilder.Entity<StatsProfile>(p =>
        {
            p.HasKey(k => k.UserId);
            if (Database.IsNpgsql())
            {
                p.HasIndex(k => k.ProfileName)
                    .HasMethod("gin")
                    .HasOperators("gin_trgm_ops")
                    .HasFilter("is_public = TRUE")
                    .IsCreatedConcurrently()
                    .HasDatabaseName("ix_stats_profile_public_name_trgm");
            }
        });

        base.OnModelCreating(modelBuilder);
    }
}

public class StatisticsDbContextFactory : IDesignTimeDbContextFactory<StatisticsDbContext>
{
    public StatisticsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<StatisticsDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;port=25432;Database=velotime;Username=velotime;Password=velotime",
            options =>
            {
                options.MigrationsHistoryTable("__ef_migrations_history", "ef");
            });
        optionsBuilder.UseSnakeCaseNamingConvention();
        return new StatisticsDbContext(optionsBuilder.Options);
    }
}
