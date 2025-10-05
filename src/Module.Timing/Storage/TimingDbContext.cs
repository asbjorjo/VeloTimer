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

        modelBuilder.Entity<Passing>();
        modelBuilder.Entity<TimingPoint>();
        modelBuilder.Entity<Transponder>();

        base.OnModelCreating(modelBuilder);
    }
}
