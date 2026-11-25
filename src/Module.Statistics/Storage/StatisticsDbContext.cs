using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
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

        modelBuilder.Entity<Sample>(e =>
        {
            e.HasIndex(s => new { s.TransponderId, s.TimingPointStartId, s.TimeStart, s.TimingPointEndId, s.TimeEnd })
                .HasDatabaseName("ux_sample_timing_identity")
                .IsUnique();
            // Completion messages lack a facility ID; do not put it before the exact times.
            e.HasIndex(s => new { s.TransponderId, s.TimeStart, s.TimeEnd })
                .HasDatabaseName("ix_sample_transponder_completion")
                .IsCreatedConcurrently();
            // Equality prefixes followed by time support backward LIMIT 1 scans; no DESC twin needed.
            e.HasIndex(s => new { s.TransponderId, s.FacilityId, s.CoursePointStartId, s.TimeStart })
                .HasDatabaseName("ix_sample_transponder_facility_start_time")
                .IsCreatedConcurrently();
            e.HasIndex(s => new { s.TransponderId, s.FacilityId, s.CoursePointEndId, s.TimeEnd })
                .HasDatabaseName("ix_sample_transponder_facility_end_time")
                .IsCreatedConcurrently();
            // No point equality in attempt evidence; keep both times as searchable index keys.
            e.HasIndex(s => new { s.TransponderId, s.FacilityId, s.TimeEnd, s.TimeStart })
                .HasDatabaseName("ix_sample_transponder_facility_attempt")
                .IsCreatedConcurrently();
        });
        modelBuilder.Entity<SampleCompleteOutbox>(e =>
        {
            e.ToTable("sample_complete_outbox");
            e.HasIndex(x => x.SampleId).IsUnique();
            var pending = e.HasIndex(x => new { x.TransponderId, x.Id });
            if (Database.IsNpgsql()) pending.HasFilter("delivered_at_utc IS NULL OR entries_created_at_utc IS NULL");
            if (Database.IsNpgsql())
            {
                e.HasIndex(x => new { x.TransponderId, x.Id }, "UnprocessedHead")
                    .HasDatabaseName("ix_sample_complete_outbox_unprocessed_head")
                    .HasFilter("entries_created_at_utc IS NULL");
                e.HasIndex(x => x.TransponderId, "ActiveSmbHead")
                    .HasDatabaseName("ux_sample_complete_outbox_active_smb_head")
                    .IsUnique().HasFilter("smb_enqueued_at_utc IS NOT NULL AND entries_created_at_utc IS NULL");
            }
        });
        modelBuilder.Entity<EntryCreatedOutbox>(e =>
        {
            e.ToTable("entry_created_outbox");
            e.HasIndex(x => x.EntryId).IsUnique();
            var pending = e.HasIndex(x => new { x.TransponderId, x.Id });
            if (Database.IsNpgsql()) pending.HasFilter("delivered_at_utc IS NULL");
            // Delivered receipts still define creation order. Never use the pending-only index
            // as the parent candidate set or treat delivery as a creation-decision checkpoint.
            e.HasIndex(x => new { x.TransponderId, x.Id }, "CreationOrder")
                .HasDatabaseName("ix_entry_created_outbox_transponder_creation_order");
            e.Property(x => x.MultiDecisionVersion).HasDefaultValue(0)
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            if (Database.IsNpgsql())
            {
                e.HasIndex(x => new { x.TransponderId, x.Id }, "UnprocessedHead")
                    .HasDatabaseName("ix_entry_created_outbox_unprocessed_head")
                    .HasFilter("multi_decisions_created_at_utc IS NULL");
                e.HasIndex(x => x.TransponderId, "ActiveSmbHead")
                    .HasDatabaseName("ux_entry_created_outbox_active_smb_head")
                    .IsUnique().HasFilter("smb_enqueued_at_utc IS NOT NULL AND multi_decisions_created_at_utc IS NULL");
                e.ToTable(t => t.HasCheckConstraint("ck_entry_created_outbox_multi_decision",
                    "multi_decision_version IN (0, 1) AND " +
                    "(multi_decision_version = 1 OR multi_decisions_created_at_utc IS NULL)"));
            }
        });
        modelBuilder.Entity<MultiCreationDecision>(e =>
        {
            e.ToTable("multi_creation_decision");
            e.HasKey(x => new { x.ParentEntryId, x.MultiConfigId });
            e.Property(x => x.ResultEntryId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            // No domain FKs: deleting a config or result must not erase a replay decision.
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
        modelBuilder.Entity<StatisticsEntry>(e =>
        {
            e.Property(s => s.IsValid).HasDefaultValue(true);
            e.Property(s => s.RequiredCoursePointsSatisfied).HasDefaultValue(true)
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
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
        modelBuilder.Entity<StatisticsValidityRevalidation>(e =>
        {
            e.HasKey(r => r.StatisticsItemId);
            e.HasOne(r => r.StatisticsItem)
                .WithMany()
                .HasForeignKey(r => r.StatisticsItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(r => r.Generation).IsConcurrencyToken();
            e.Property(r => r.MultiRescanPending).HasDefaultValue(false);
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
