using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VeloTime.Module.Common;
using VeloTime.Module.Profiles.Model;

namespace VeloTime.Module.Profiles.Storage;

public sealed class ProfilesDbContext(DbContextOptions<ProfilesDbContext> options) : BaseDbContext(options)
{
    public DbSet<LanguagePreference> LanguagePreferences => Set<LanguagePreference>();

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

        base.OnModelCreating(modelBuilder);
    }
}

public sealed class ProfilesDbContextFactory : IDesignTimeDbContextFactory<ProfilesDbContext>
{
    public ProfilesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ProfilesDbContext>();
        options.UseNpgsql(
            Environment.GetEnvironmentVariable("ConnectionStrings__profilesdb")
                ?? "Host=localhost;Port=25432;Database=profilesdb;Username=velotime",
            postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "ef"));
        options.UseSnakeCaseNamingConvention();
        return new ProfilesDbContext(options.Options);
    }
}
