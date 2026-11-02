using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using SlimMessageBus;
using System.Diagnostics;
using VeloTime.Agent.Logging;
using VeloTime.Agent.Model;
using VeloTime.Agent.Storage;

namespace VeloTime.Agent.Service;

public class MessagingService(IMessageBus messageBus, AgentDbContext dbContext, Metrics metrics, ILogger<MessagingService> log)
{
    public Task SendEventAsync(InstallationConfig config) => SendEventAsync(config, CancellationToken.None);

    public Task SendEventAsync(InstallationConfig config, CancellationToken cancellationToken) =>
        EnqueueAsync([config], m => m.ToMessage(), "Installation Config", cancellationToken);

    public Task SendEventsAsync(IEnumerable<LoopConfig> config) => SendEventsAsync(config, CancellationToken.None);

    public Task SendEventsAsync(IEnumerable<LoopConfig> config, CancellationToken cancellationToken) =>
        EnqueueAsync(config, m => m.ToMessage(), "Loop Config", cancellationToken);

    public Task SendEventAsync(LoopStatus status) => SendEventAsync(status, CancellationToken.None);

    public Task SendEventAsync(LoopStatus status, CancellationToken cancellationToken) =>
        EnqueueAsync([status], m => m.ToMessage(), "Loop Status", cancellationToken);

    public Task SendEventsAsync(IEnumerable<Passing> passing) => SendEventsAsync(passing, CancellationToken.None);

    public Task SendEventsAsync(IEnumerable<Passing> passing, CancellationToken cancellationToken) =>
        EnqueueAsync(passing, m => m.ToMessage(), "Passings", cancellationToken, PreparePassingsAsync, processedPassing: true);

    public Task SendEventsAsync(IEnumerable<SegmentConfig> config) => SendEventsAsync(config, CancellationToken.None);

    public Task SendEventsAsync(IEnumerable<SegmentConfig> config, CancellationToken cancellationToken) =>
        EnqueueAsync(config, m => m.ToMessage(), "Segment Config", cancellationToken);

    public Task SendEventsAsync(IEnumerable<SystemConfig> config) => SendEventsAsync(config, CancellationToken.None);

    public Task SendEventsAsync(IEnumerable<SystemConfig> config, CancellationToken cancellationToken) =>
        EnqueueAsync(config, m => m.ToMessage(), "System Config", cancellationToken);

    private async Task<Passing[]> PreparePassingsAsync(Passing[] batch, CancellationToken cancellationToken)
    {
        var unique = batch.DistinctBy(p => p.Id).ToArray();
        var ids = unique.Select(p => p.Id).ToArray();
        var persisted = await dbContext.Set<Passing>().AsNoTracking()
            .Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToArrayAsync(cancellationToken);
        var pending = unique.ExceptBy(persisted, p => p.Id).ToArray();
        await dbContext.AddRangeAsync(pending, cancellationToken);
        return pending;
    }

    private async Task EnqueueAsync<TModel, TMessage>(IEnumerable<TModel> input, Func<TModel, TMessage> map,
        string operation, CancellationToken cancellationToken,
        Func<TModel[], CancellationToken, Task<TModel[]>>? prepare = null, bool processedPassing = false)
        where TMessage : class
    {
        using var activity = Instrumentation.Source.StartActivity($"Send {operation}");
        IDbContextTransaction? transaction = null;
        var count = 0;
        var accepted = 0;
        try
        {
            var batch = input.ToArray();
            count = batch.Length;
            activity?.SetTag("count", count);
            if (count == 0) return;
            log.LogSendingEvents(operation, count);
            transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            if (prepare != null) batch = await prepare(batch, cancellationToken);
            foreach (var item in batch)
            {
                // Collection publishes bypass SlimMessageBus 3.4 outbox interceptors.
                await messageBus.Publish(map(item), cancellationToken: cancellationToken);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            accepted = batch.Length;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            log.LogError(exception, "Failed to durably enqueue {Count} events for {Operation}.", count, operation);
            try
            {
                if (transaction != null) await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                log.LogError(rollbackException, "Rollback failed for {Operation}.", operation);
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }
        activity?.SetStatus(ActivityStatusCode.Ok);
        if (processedPassing) metrics.ProcessedPassing(accepted, activity);
    }
}
