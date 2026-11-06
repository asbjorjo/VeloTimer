using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Npgsql;

namespace VeloTime.Module.Statistics.Service;

public sealed class StatisticsRevalidationOptions
{
    public const string Section = "Statistics:Revalidation";

    /// <summary>Entries scanned per short transaction.</summary>
    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 500;

    /// <summary>Fallback poll interval when no wake-up arrives; durable rows are the source of work.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromSeconds(30);

    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan FailureDelay { get; set; } = TimeSpan.FromSeconds(5);

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan MaxFailureDelay { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>In-process wake-up hint for the poller. Coalesces; losing a signal only delays work.</summary>
public sealed class StatisticsRevalidationSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => channel.Writer.TryWrite(true);

    /// <summary>Waits for a wake-up or the timeout, whichever comes first.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await channel.Reader.WaitToReadAsync(timeoutSource.Token);
            channel.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Poll interval elapsed without a wake-up.
        }
    }
}

/// <summary>
/// Bounded durable poller: bootstraps missing jobs once per item at startup (failure stops the host),
/// then processes one short batch per pending item in round-robin uuid order. Threshold events are
/// wake-ups only and never reset cursors. Failed batches roll back, are logged and counted, and the job
/// stays pending for retry with backoff; no broker lock is held during a sweep.
/// </summary>
public sealed class StatisticsRevalidationWorker(
    IServiceScopeFactory scopes,
    StatisticsRevalidationSignal signal,
    IOptions<StatisticsRevalidationOptions> options,
    Metrics metrics,
    ILogger<StatisticsRevalidationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        await BootstrapAsync(stoppingToken);

        Guid? after = null;
        var failureDelay = settings.FailureDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid? itemId = null;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var revalidation = scope.ServiceProvider.GetRequiredService<StatisticsRevalidationService>();
                itemId = await revalidation.FindNextPendingItemAsync(after, stoppingToken);
                if (itemId is null)
                {
                    after = null;
                    await signal.WaitAsync(settings.IdlePollInterval, stoppingToken);
                    continue;
                }
                // Advance round-robin before processing so a failing item cannot starve the others.
                after = itemId;
                var result = await revalidation.ProcessBatchAsync(itemId.Value, settings.BatchSize, stoppingToken);
                failureDelay = settings.FailureDelay;
                if (result.Outcome == StatisticsRevalidationBatchOutcome.Completed)
                    logger.LogInformation("Validity revalidation of statistics item {StatisticsItemId} generation {Generation} completed",
                        itemId, result.Generation);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                var transient = IsTransient(exception);
                metrics.RevalidationFailed(transient);
                if (transient)
                {
                    logger.LogWarning(exception,
                        "Transient conflict revalidating statistics item {StatisticsItemId}; batch rolled back and will be retried", itemId);
                    await DelayAsync(settings.FailureDelay, stoppingToken);
                    continue;
                }
                logger.LogError(exception,
                    "Validity revalidation batch for statistics item {StatisticsItemId} failed; batch rolled back, job remains pending, retrying in {Delay}",
                    itemId, failureDelay);
                await DelayAsync(failureDelay, stoppingToken);
                failureDelay = TimeSpan.FromTicks(Math.Min(failureDelay.Ticks * 2, settings.MaxFailureDelay.Ticks));
            }
        }
    }

    private async Task BootstrapAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var inserted = await scope.ServiceProvider.GetRequiredService<StatisticsRevalidationService>().BootstrapAsync(stoppingToken);
            logger.LogInformation("Statistics validity revalidation bootstrap created {Count} pending item jobs", inserted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            logger.LogCritical(exception, "Statistics validity revalidation bootstrap failed; stopping the processor");
            throw;
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try { await Task.Delay(delay, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    /// <summary>Deadlock, serialization failure, lock timeout or generation supersession.</summary>
    internal static bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException) return true;
            if (current is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or
                PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.LockNotAvailable }) return true;
        }
        return false;
    }
}
