using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VeloTime.Module.Common.Operations;

/// <summary>Where a SlimMessageBus PostgreSQL outbox lives and how often to sample it.</summary>
public sealed record OutboxMonitorTarget(string Name, string Schema, TimeSpan SampleInterval, string Table = "smb_outbox");

/// <summary>
/// Samples a SlimMessageBus PostgreSQL outbox (pending count, oldest undelivered age, aborted count) into
/// <see cref="MessagingMetrics"/>. A growing oldest age means the outbox cannot deliver, usually because the
/// bus is unreachable or the oldest message keeps failing (the outboxes deliver in order, one at a time).
/// </summary>
public sealed class OutboxMonitor<TContext>(
    IServiceScopeFactory scopes,
    MessagingMetrics metrics,
    OutboxMonitorTarget target,
    ILogger<OutboxMonitor<TContext>> logger,
    TimeProvider? timeProvider = null) : BackgroundService where TContext : DbContext
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        MonitorLoop.RunAsync(target.SampleInterval, time, SampleAsync, exception =>
        {
            metrics.SampleFailed($"outbox:{target.Name}");
            logger.LogWarning(exception, "Outbox {Outbox} sample failed", target.Name);
        }, stoppingToken);

    public async Task SampleAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<TContext>();
        // Schema and table come from code, never from user input.
#pragma warning disable EF1002
        var row = (await storage.Database.SqlQueryRaw<OutboxRow>($"""
            SELECT count(*) FILTER (WHERE NOT delivery_complete AND NOT delivery_aborted) AS "Pending",
                   count(*) FILTER (WHERE delivery_aborted) AS "Aborted",
                   min(timestamp) FILTER (WHERE NOT delivery_complete AND NOT delivery_aborted) AS "Oldest"
            FROM "{target.Schema}"."{target.Table}"
            """).ToListAsync(cancellationToken)).Single();
#pragma warning restore EF1002
        var oldest = row.Oldest is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : (DateTime?)null;
        metrics.ObserveOutbox(target.Name, row.Pending, row.Aborted, oldest, time.GetUtcNow().UtcDateTime);
    }

    private sealed record OutboxRow(long Pending, long Aborted, DateTime? Oldest);
}

/// <summary>Shared sampling loop for the operations monitors.</summary>
public static class MonitorLoop
{
    /// <summary>Samples now and then every interval until stopped; a failed sample is reported and skipped.</summary>
    public static async Task RunAsync(TimeSpan interval, TimeProvider time, Func<CancellationToken, Task> sample,
        Action<Exception> failed, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval, time);
        do
        {
            try
            {
                await sample(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                failed(exception);
            }
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        } while (true);
    }
}
