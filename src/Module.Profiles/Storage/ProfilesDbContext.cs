using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VeloTime.Module.Common;
using VeloTime.Module.Profiles.Model;

namespace VeloTime.Module.Profiles.Storage;

public sealed class ProfilesDbContext(DbContextOptions<ProfilesDbContext> options) : BaseDbContext(options)
{
    public DbSet<LanguagePreference> LanguagePreferences => Set<LanguagePreference>();
    public DbSet<DisplayPreference> DisplayPreferences => Set<DisplayPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("profiles");

        modelBuilder.Entity<LanguagePreference>(entity =>
        {
            entity.ToTable("language_preference", table =>
                table.HasCheckConstraint("ck_language_preference_language", "language IN ('en', 'nb-NO')"));
            entity.HasKey(preference => new { preference.Issuer, preference.Subject });
            // Identity values are opaque, case-sensitive strings, not normalized URLs or user IDs.
            entity.Property(preference => preference.Issuer).UseCollation("C");
            entity.Property(preference => preference.Subject).UseCollation("C");
            entity.Property(preference => preference.Language).IsRequired().UseCollation("C");
        });

        modelBuilder.Entity<DisplayPreference>(entity =>
        {
            entity.ToTable("display_preference", table =>
            {
                table.HasCheckConstraint(
                    "ck_display_preference_distance_unit",
                    "distance_unit IN ('m', 'ft')");
                table.HasCheckConstraint(
                    "ck_display_preference_speed_unit",
                    "speed_unit IN ('km/h', 'mph', 'm/s')");
                table.HasCheckConstraint(
                    "ck_display_preference_time_mode_zone",
                    "(time_mode = 'timeZone' AND time_zone_id IS NOT NULL) OR (time_mode IN ('browser', 'utc') AND time_zone_id IS NULL)");
                table.HasCheckConstraint(
                    "ck_display_preference_time_mode",
                    "time_mode IN ('browser', 'utc', 'timeZone')");
            });
            entity.HasKey(preference => new { preference.Issuer, preference.Subject });
            // Identity values are opaque, case-sensitive strings, not normalized URLs or user IDs.
            entity.Property(preference => preference.Issuer).UseCollation("C");
            entity.Property(preference => preference.Subject).UseCollation("C");
            entity.Property(preference => preference.DistanceUnit).IsRequired().UseCollation("C");
            entity.Property(preference => preference.SpeedUnit).IsRequired().UseCollation("C");
            entity.Property(preference => preference.TimeMode).IsRequired().UseCollation("C");
            entity.Property(preference => preference.TimeZoneId).UseCollation("C");
        });

        base.OnModelCreating(modelBuilder);
    }
}

public sealed class ProfilesDbContextFactory : IDesignTimeDbContextFactory<ProfilesDbContext>
{
    public ProfilesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ProfilesDbContext>();
        options.UseNpgsql(
            Environment.GetEnvironmentVariable("ConnectionStrings__velotimedb")
                ?? "Host=localhost;Port=25432;Database=velotimedb;Username=velotime",
            postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "ef"));
        options.UseSnakeCaseNamingConvention();
        return new ProfilesDbContext(options.Options);
    }
}
