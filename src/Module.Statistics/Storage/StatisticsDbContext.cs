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

    public DbSet<HallOfFameSettings> HallOfFameSettingsDocuments => Set<HallOfFameSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("statistics");
        if (Database.IsNpgsql())
        {
            // PostgreSQL literal substring searches on public profile names use gin_trgm_ops.
            modelBuilder.HasPostgresExtension("public", "pg_trgm");
        }

        modelBuilder.Entity<Sample>();
        modelBuilder.Entity<StatisticsItem>();
        modelBuilder.Entity<HallOfFameSettings>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id)
                .ValueGeneratedNever();
            e.Property(s => s.Version)
                .IsConcurrencyToken();
            e.Property(s => s.SectionsJson)
                .IsRequired();
            if (Database.IsNpgsql())
            {
                e.Property(s => s.SectionsJson)
                    .HasColumnType("jsonb");
            }

            e.ToTable("hall_of_fame_settings", table =>
            {
                table.HasCheckConstraint("ck_hall_of_fame_settings_singleton", "id = 1");
                table.HasCheckConstraint("ck_hall_of_fame_settings_version_nonnegative", "version >= 0");
                if (Database.IsNpgsql())
                {
                    table.HasCheckConstraint(
                        "ck_hall_of_fame_settings_sections_array",
                        "jsonb_typeof(sections_json) = 'array'");
                }
            });
            e.HasData(new HallOfFameSettings
            {
                Id = HallOfFameSettings.SingletonId,
                Version = 0,
                SectionsJson = "[]"
            });
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
