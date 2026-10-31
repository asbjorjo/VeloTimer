using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;
using VeloTime.Agent.Model;

namespace VeloTime.Agent.Storage;

public class AgentDbContext : DbContext
{
    public AgentDbContext(DbContextOptions<AgentDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("agent");

        modelBuilder.Entity<Passing>().HasKey(x => x.Id);
    }
}

public class AgentDbContextFactory : IDesignTimeDbContextFactory<AgentDbContext>
{
    public AgentDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AgentDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;port=25432;Database=velotime;Username=velotime;Password=velotime",
                x => { x.MigrationsHistoryTable("__Migrations", "agent"); }
                );
        optionsBuilder.UseSnakeCaseNamingConvention();
        return new AgentDbContext(optionsBuilder.Options);
    }
}
