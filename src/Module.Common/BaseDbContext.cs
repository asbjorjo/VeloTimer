using Microsoft.EntityFrameworkCore;

namespace VeloTime.Module.Common;

public abstract class BaseDbContext : DbContext
{
    protected BaseDbContext(DbContextOptions options) : base(options)
    {
    }

    //protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    //    => optionsBuilder.UseSnakeCaseNamingConvention();
}
