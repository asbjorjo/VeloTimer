using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VeloTime.Agent.Storage;

namespace VeloTime.Agent;

public static class MigrateExtensions
{
    public static void ApplyMigrations(this IHost host)
    {
        var logger = host.Services.GetRequiredService<ILogger<AgentDbContext>>();
        try
        {
            using var scope = host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            logger.LogInformation("Applying database migrations...");
            dbContext.Database.Migrate();
            logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while applying database migrations.");
            throw;
        }
    }
}
