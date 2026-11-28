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

        modelBuilder.Entity<Passing>(e =>
        {
            e.HasKey(x => x.Id);
            // AgentStore replays read passings by time range.
            e.HasIndex(x => new { x.Time, x.Id });
        });
        modelBuilder.Entity<ReplayRequest>(e =>
        {
            e.ToTable("replay_request");
            e.HasKey(x => x.RequestId);
            e.Property(x => x.LoopIds).HasConversion(
                ids => string.Join('\n', ids),
                text => text.Length == 0 ? new List<string>() : text.Split('\n', StringSplitOptions.None).ToList(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                    (a, b) => a!.SequenceEqual(b!), ids => string.Join('\n', ids).GetHashCode(), ids => ids.ToList()));
            e.Property(x => x.Mode).HasMaxLength(32);
            e.Property(x => x.State).HasMaxLength(32);
            e.Ignore(x => x.IsFinished);
            e.HasIndex(x => x.State);
        });
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
