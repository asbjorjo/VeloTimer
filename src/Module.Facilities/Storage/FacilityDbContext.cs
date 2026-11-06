using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Model;

namespace VeloTime.Module.Facilities.Storage;

public class FacilityDbContext : BaseDbContext
{
    public FacilityDbContext(DbContextOptions<FacilityDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("facility");

        modelBuilder.Entity<Facility>();

        modelBuilder.Entity<CourseLayout>()
            .HasOne(l => l.Facility).WithMany(f => f.Layouts)
            .HasForeignKey(l => l.FacilityId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CourseSegment>()
            .HasOne(s => s.CourseLayout).WithMany(l => l.Segments)
            .HasForeignKey(s => s.CourseLayoutId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CourseSegment>()
            .HasOne(s => s.Start).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CourseSegment>()
            .HasOne(s => s.End).WithMany().OnDelete(DeleteBehavior.Restrict);

        var association = modelBuilder.Entity<CoursePointTimingAssociation>();
        // Explicit names keep the portable CHECK usable in SQLite without naming conventions.
        association.ToTable("course_point_timing_association", table =>
            table.HasCheckConstraint("ck_course_point_timing_association_interval",
                "valid_from_utc IS NULL OR valid_to_utc IS NULL OR valid_from_utc < valid_to_utc"));
        association.Property(a => a.ValidFromUtc).HasColumnName("valid_from_utc")
            .UsePropertyAccessMode(PropertyAccessMode.Property);
        association.Property(a => a.ValidToUtc).HasColumnName("valid_to_utc")
            .UsePropertyAccessMode(PropertyAccessMode.Property);
        association.HasOne(a => a.CoursePoint).WithMany(p => p.TimingAssociations)
            .HasForeignKey(a => a.CoursePointId).OnDelete(DeleteBehavior.Restrict);
        association.HasIndex(a => new { a.CoursePointId, a.ValidFromUtc, a.ValidToUtc });
        association.HasIndex(a => new { a.TimingPointId, a.ValidFromUtc, a.ValidToUtc });

        if (Database.IsNpgsql())
        {
            modelBuilder.HasPostgresExtension("public", "btree_gist");
            // Npgsql uses timestamp with time zone. Do not accept PostgreSQL's explicit +/-infinity
            // timestamp values as finite bounds; only SQL NULL represents an unbounded endpoint.
            association.ToTable("course_point_timing_association", table =>
                table.HasCheckConstraint("ck_course_point_timing_association_finite_bounds",
                    "(valid_from_utc IS NULL OR isfinite(valid_from_utc)) AND " +
                    "(valid_to_utc IS NULL OR isfinite(valid_to_utc))"));
        }

        base.OnModelCreating(modelBuilder);
    }
}

public class FacilityDbContextFactory : IDesignTimeDbContextFactory<FacilityDbContext>
{
    public FacilityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FacilityDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;port=25432;Database=velotime;Username=velotime;Password=velotime",
            options =>
            {
                options.MigrationsHistoryTable("__ef_migrations_history", "ef");
            });
        optionsBuilder.UseSnakeCaseNamingConvention();
        return new FacilityDbContext(optionsBuilder.Options);
    }
}
