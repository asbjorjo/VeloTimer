using Microsoft.EntityFrameworkCore;
using SlimMessageBus;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

/// <summary>Claims only the oldest pending event per transponder; never intercepts other bus publications.</summary>
public sealed class SampleCompleteOutboxDispatcher(StatisticsDbContext storage, IMessageBus bus)
{
    public async Task<bool> DispatchOneAsync(CancellationToken cancellationToken)
    {
        await using var tx = await storage.Database.BeginTransactionAsync(cancellationToken);
        // The lock is held across broker acknowledgement. A crash or uncertain acknowledgement
        // rolls back the mark and causes an at-least-once redelivery.
        var row = await storage.Set<SampleCompleteOutbox>().FromSqlRaw("""
            SELECT o.* FROM statistics.sample_complete_outbox o
            WHERE o.delivered_at_utc IS NULL
              AND NOT EXISTS (
                SELECT 1 FROM statistics.sample_complete_outbox earlier
                WHERE earlier.transponder_id = o.transponder_id
                  AND (earlier.delivered_at_utc IS NULL OR earlier.entries_created_at_utc IS NULL)
                  AND earlier.id < o.id)
            ORDER BY o.id
            LIMIT 1 FOR UPDATE OF o SKIP LOCKED
            """).SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        await bus.Publish(new SampleComplete(row.TransponderId, row.TimeStart, row.TimeEnd,
            row.CoursePointStart, row.CoursePointEnd, row.Distance), cancellationToken: cancellationToken);
        row.DeliveredAtUtc = DateTime.UtcNow;
        await storage.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }
}

public sealed class SampleCompleteOutboxWorker(
    IServiceScopeFactory scopes, ILogger<SampleCompleteOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<SampleCompleteOutboxDispatcher>();
                if (!await dispatcher.DispatchOneAsync(stoppingToken))
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "SampleComplete outbox delivery failed; will retry");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
