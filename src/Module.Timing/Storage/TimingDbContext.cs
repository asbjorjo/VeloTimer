using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Storage;

public class TimingDbContext : DbContext
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
            e.HasIndex(p => new {p.TransponderId, p.Time, p.TimingPointId}).IsUnique();
        });

        modelBuilder.Entity<TimingPoint>(e =>
        {
            e.HasIndex(tp => new { tp.InstallationId, tp.SystemId }).IsUnique();
        });

        modelBuilder.Entity<Transponder>(e =>
        {
            e.HasDiscriminator(t => t.System)
                .HasValue<MylapsX2Transponder>(TimingSystem.MyLaps_X2);
            e.HasIndex(t => new { t.System, t.SystemId }).IsUnique();
        });

        modelBuilder.Entity<Installation>(e =>
        {
            e.HasMany(i => i.TimingPoints)
                .WithOne(tp => tp.Installation)
                .IsRequired();

        });
        modelBuilder.Entity<Sample>(e =>
        {
            e.HasOne(s => s.Start)
                .WithMany()
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.End)
                .WithMany()
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.StartId, s.EndId }).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}

public class TimingDbContextFactory : IDesignTimeDbContextFactory<TimingDbContext>
{
    public TimingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TimingDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;port=25432;Database=velotime;Username=velotime;Password=velotime");
        optionsBuilder.UseSnakeCaseNamingConvention();
        return new TimingDbContext(optionsBuilder.Options);
    }
}
