using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VeloTime.Module.Facilities.Model;

namespace VeloTime.Module.Facilities.Storage;

public class FacilityDbContext : DbContext
{
    public FacilityDbContext(DbContextOptions<FacilityDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("facility");

        modelBuilder.Entity<Facility>();

        base.OnModelCreating(modelBuilder);
    }
}

public class FacilityDbContextFactory : IDesignTimeDbContextFactory<FacilityDbContext>
{
    public FacilityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FacilityDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;port=25432;Database=velotime;Username=velotime;Password=velotime");
        optionsBuilder.UseSnakeCaseNamingConvention();
        return new FacilityDbContext(optionsBuilder.Options);
    }
}
