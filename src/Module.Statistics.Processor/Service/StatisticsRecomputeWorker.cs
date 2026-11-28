using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace VeloTime.Module.Statistics.Service;

/// <summary>In-process wake-up hint for the recompute worker. Coalesces; losing a signal only delays work.</summary>
public sealed class StatisticsRecomputeSignal
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
/// Drains recompute requests (plan §3.3): one transponder per step, each in its own short transaction.
/// Request rows are the source of work; the signal only shortens the idle wait. Several replicas can run
/// side by side; row locks with SKIP LOCKED and the transponder lock keep them apart.
/// </summary>
public sealed class StatisticsRecomputeWorker(
    IServiceScopeFactory scopes,
    StatisticsRecomputeSignal signal,
    IOptions<StatisticsRecomputeOptions> options,
    ILogger<StatisticsRecomputeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<StatisticsRecomputeService>().ProcessNextAsync(stoppingToken);
                if (result.Outcome == RecomputeStepOutcome.Idle)
                    await signal.WaitAsync(options.Value.IdlePollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Claiming failed (for example the database is unavailable); nothing was changed.
                logger.LogError(exception, "Statistics recompute step failed before claiming work; retrying in {Delay}",
                    options.Value.FailureDelay);
                try { await Task.Delay(options.Value.FailureDelay, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
