using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Interface.Data;
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

        modelBuilder.Entity<PassingFact>(e =>
        {
            e.HasKey(p => p.PassingId);
            // Session expansion and loading scan one transponder's facts by time.
            e.HasIndex(p => new { p.TransponderId, p.Time, p.PassingId })
                .HasDatabaseName("ix_passing_fact_transponder_time");
            e.HasIndex(p => new { p.Time, p.TransponderId })
                .HasDatabaseName("ix_passing_fact_time_transponder");
        });
        modelBuilder.Entity<RecomputeRequest>(e =>
        {
            e.Property(r => r.Id).UseIdentityByDefaultColumn();
            e.Property(r => r.Reason).HasConversion<string>().HasMaxLength(32);
            e.Property(r => r.LastError).HasMaxLength(2000);
            e.HasIndex(r => new { r.TransponderId, r.Id }).HasDatabaseName("ix_recompute_request_transponder_id");
            e.HasIndex(r => new { r.NotBeforeUtc, r.Id }).HasDatabaseName("ix_recompute_request_not_before_id");
        });
        modelBuilder.Entity<ManualDecisionReview>(e =>
        {
            e.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(32);
            e.Property(r => r.Note).HasMaxLength(500);
            e.HasIndex(r => new { r.CreatedAtUtc, r.Id }).HasDatabaseName("ix_manual_decision_review_created_id");
            e.HasIndex(r => r.EntryId).HasDatabaseName("ix_manual_decision_review_entry");
        });
        modelBuilder.Entity<Sample>(e =>
        {
            // Deterministic identity: one sample per pair of consecutive passings.
            e.HasIndex(s => new { s.StartPassingId, s.EndPassingId })
                .IsUnique()
                .HasDatabaseName("ux_sample_passings");
            // The recompute worker diffs one transponder's samples by time range.
            e.HasIndex(s => new { s.TransponderId, s.TimeStart, s.TimeEnd })
                .HasDatabaseName("ix_sample_transponder_time");
            e.HasIndex(s => new { s.TransponderId, s.FacilityId, s.TimeEnd, s.TimeStart })
                .HasDatabaseName("ix_sample_transponder_facility_attempt");
        });
        modelBuilder.Entity<StatisticsItem>(e =>
        {
            e.Property(item => item.DistanceMode).HasDefaultValue(StatisticsDistanceMode.Fixed);
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
            e.Property(s => s.RequiredCoursePointIds).IsRequired();
            if (Database.IsNpgsql())
            {
                e.Property(s => s.RequiredCoursePointIds).HasDefaultValue(Array.Empty<Guid>());
            }

        });
        modelBuilder.Entity<MultiStatisticsItemConfig>(e =>
        {
            e.HasOne(m => m.StatisticsItem)
                .WithMany()
                .IsRequired()
                .OnDelete(DeleteBehavior.ClientNoAction);
            e.HasIndex(m => new { m.StatisticsItemId, m.ParentConfigId });
        });
        modelBuilder.Entity<StatisticsConfigVersion>(e =>
        {
            e.HasOne(v => v.StatisticsItemConfig)
                .WithMany()
                .HasForeignKey(v => v.StatisticsItemConfigId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(v => v.DistanceMode).HasDefaultValue(StatisticsDistanceMode.Fixed);
            e.Property(v => v.RequiredCoursePointIds).IsRequired();
            e.HasIndex(v => new { v.StatisticsItemConfigId, v.ValidFromUtc }).IsUnique()
                .HasDatabaseName("ux_statistics_config_version_config_from");
            e.HasIndex(v => v.StatisticsItemId).HasDatabaseName("ix_statistics_config_version_item");
            if (Database.IsNpgsql())
            {
                e.ToTable(t => t.HasCheckConstraint("ck_statistics_config_version_interval",
                    "valid_from_utc IS NULL OR valid_to_utc IS NULL OR valid_from_utc < valid_to_utc"));
            }
        });
        modelBuilder.Entity<StatisticsEntry>(e =>
        {
            e.Property(s => s.IsValid).HasDefaultValue(true);
            e.Property(s => s.RequiredCoursePointsSatisfied).HasDefaultValue(true);
            e.Property(s => s.ManualInvalidationNote).HasMaxLength(500);
            if (Database.IsNpgsql())
            {
                e.ToTable(t => t.HasCheckConstraint("ck_statistics_entry_validity",
                    "(is_valid AND invalid_reason IS NULL) OR " +
                    "(NOT is_valid AND invalid_reason IS NOT NULL AND invalid_reason IN (0, 1, 2, 3, 4, 5)) AND " +
                    "(invalid_reason IS DISTINCT FROM 5 OR " +
                    "(manual_invalidation_changed_by_user_id IS NOT NULL AND " +
                    "manual_invalidation_changed_at_utc IS NOT NULL))"));
            }
            e.HasIndex(s => new { s.StatisticsItemId, s.Id })
                .IsCreatedConcurrently();
            // Unfiltered administrative pages need time-first ordering without equality prefixes.
            // One B-tree supports newest-first backward and previous-page forward scans.
            e.HasIndex(s => new { s.TimeEnd, s.Id })
                .IsCreatedConcurrently()
                .HasDatabaseName("ix_statistics_entry_time_end_id");
            // One non-partial B-tree per sort supports both directions and UUID ties.
            e.HasIndex(s => new { s.Duration, s.Id })
                .IsCreatedConcurrently()
                .HasDatabaseName("ix_statistics_entry_duration_id");
            e.HasIndex(s => new { s.Speed, s.Id })
                .IsCreatedConcurrently()
                .HasDatabaseName("ix_statistics_entry_speed_id");
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
