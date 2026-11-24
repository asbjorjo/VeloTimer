using Microsoft.EntityFrameworkCore;
using SlimMessageBus;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

/// <summary>Isolated from both the API SMB outbox and the upstream SampleComplete dispatcher.</summary>
public sealed class EntryCreatedOutboxDispatcher(StatisticsDbContext storage, IMessageBus bus)
{
    public async Task<bool> DispatchOneAsync(CancellationToken cancellationToken)
    {
        await using var tx = await storage.Database.BeginTransactionAsync(cancellationToken);
        var row = await storage.Set<EntryCreatedOutbox>().FromSqlRaw("""
            SELECT o.* FROM statistics.entry_created_outbox o
            WHERE o.delivered_at_utc IS NULL
              AND NOT EXISTS (
                SELECT 1 FROM statistics.entry_created_outbox earlier
                WHERE earlier.transponder_id = o.transponder_id
                  AND earlier.delivered_at_utc IS NULL AND earlier.id < o.id)
            ORDER BY o.id
            LIMIT 1 FOR UPDATE OF o SKIP LOCKED
            """).SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        // Direct send via the existing producer: MessageName, topic and SessionId remain unchanged.
        // Hold the row lock until acknowledged AND committed. Uncertain outcomes must resend.
        if (row.EntryId == Guid.Empty)
            throw new InvalidOperationException("EntryCreated outbox receipt has an empty entry ID.");
        await bus.Publish(new EntryCreated(row.TransponderId, row.TimeStart, row.TimeEnd,
            row.StatisticsItemId, row.ConfigItemId, row.EntryId),
            headers: row.Single ? new Dictionary<string, object> { ["Single"] = true } : null,
            cancellationToken: cancellationToken);
        row.DeliveredAtUtc = DateTime.UtcNow;
        await storage.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }
}

public sealed class EntryCreatedOutboxWorker(
    IServiceScopeFactory scopes, ILogger<EntryCreatedOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<EntryCreatedOutboxDispatcher>()
                    .DispatchOneAsync(stoppingToken))
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "EntryCreated outbox delivery failed; will retry");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
