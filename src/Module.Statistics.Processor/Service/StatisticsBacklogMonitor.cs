using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using VeloTime.Module.Common.Operations;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

public sealed class StatisticsBacklogMonitorOptions
{
    public const string Section = "Statistics:Operations";

    /// <summary>How often the backlog gauges are refreshed from the database.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Samples the projection's backlog (dirty requests, oldest dirty age, unresolved passings, review backlog) into
/// the Statistics backlog gauges (plan Phase 5). Every replica samples the same database, so dashboards and
/// alerts should aggregate these gauges with max, not sum.
/// </summary>
public sealed class StatisticsBacklogMonitor(
    IServiceScopeFactory scopes,
    Metrics metrics,
    IOptions<StatisticsBacklogMonitorOptions> options,
    ILogger<StatisticsBacklogMonitor> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // A failed sample leaves the gauges on the last good one.
        MonitorLoop.RunAsync(options.Value.SampleInterval, time, SampleAsync,
            exception => logger.LogWarning(exception, "Statistics backlog sample failed"), stoppingToken);

    public async Task SampleAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        var snapshot = await StatisticsOperations.ReadBacklogAsync(storage, cancellationToken);
        metrics.ObserveBacklog(snapshot, time.GetUtcNow().UtcDateTime);
    }
}
