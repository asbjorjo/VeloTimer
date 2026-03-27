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

        modelBuilder.Entity<Sample>();
        modelBuilder.Entity<StatisticsItem>();
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
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => new { s.StatisticsItemId, s.TransponderId, s.TimeStart, s.TimeEnd });
        });
        modelBuilder.Entity<StatsProfile>(p =>
        {
            p.HasKey(k => k.UserId);
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
