using Microsoft.EntityFrameworkCore;
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

        modelBuilder.Entity<Passing>(p =>
        {
            p.HasOne(e => e.TimingPoint)
                .WithMany()
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
            p.HasOne(e => e.Transponder)
                .WithMany()
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<TimingPoint>();
        modelBuilder.Entity<Transponder>();
        modelBuilder.Entity<Installation>()
            .HasMany(e => e.TimingPoints)
            .WithOne(e => e.Installation)
            .IsRequired();
        modelBuilder.Entity<TransponderType>(tt =>
        {
            tt.HasMany<Transponder>()
                .WithOne(e => e.Type)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<TimingSystem>(t =>
        {
            t.HasMany(e => e.Installations)
                .WithOne(e => e.TimingSystem)
                .OnDelete(DeleteBehavior.Restrict);
        });

        base.OnModelCreating(modelBuilder);
    }
}
