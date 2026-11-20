using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using VeloTime.Module.Timing.Storage;

namespace Module.Timing.Migration;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    public const string ActivitySourceName = "VeloTime.Module.Timing.Migration";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = ActivitySource.StartActivity("Applying Migrations", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TimingDbContext>();

            await ApplyMigrationsAsync(dbContext, stoppingToken);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
        
        hostApplicationLifetime.StopApplication();
    }

    private static async Task ApplyMigrationsAsync(TimingDbContext dbContext, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        });
    }

    private static async Task SeedDataAsync(TimingDbContext dbContext, CancellationToken cancellationToken)
    {
        // Implement data seeding logic here if needed
        await Task.CompletedTask;
    }
}
