using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VeloTime.MessageBus.Topology;

internal sealed class TopologyWorker(
    MessageBusTopologyReconciler reconciler,
    IOptions<MessageBusTopologyOptions> topology,
    IHostApplicationLifetime lifetime,
    ILogger<TopologyWorker> logger) : BackgroundService
{
    public const string ActivitySourceName = "VeloTime.MessageBus.Topology";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = ActivitySource.StartActivity("Provision message bus topology", ActivityKind.Client);

        try
        {
            await reconciler.ReconcileAsync(topology.Value, stoppingToken);
            stoppingToken.ThrowIfCancellationRequested();
            logger.LogInformation("Message bus topology is up to date ({TopicCount} topics)", topology.Value.Topics.Count);
        }
        catch (Exception ex)
        {
            // StopHost can shut down normally after a background-service failure.
            Environment.ExitCode = 1;
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }

        lifetime.StopApplication();
    }
}
