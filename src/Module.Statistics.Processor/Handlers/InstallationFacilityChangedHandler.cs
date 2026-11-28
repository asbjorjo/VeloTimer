using SlimMessageBus;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Statistics.Handlers;

/// <summary>
/// An installation's dated facility assignments changed (plan §4.2, C2). Evicts the cached assignment history
/// first and only then marks every transponder with facts at that installation in the changed interval dirty
/// (evict-before-enqueue, plan §4.3). If eviction fails the message is retried, so no recompute can read the
/// history from before the change. Redelivery only marks the same range dirty again.
/// </summary>
public sealed class InstallationFacilityChangedHandler(
    StatisticsDbContext storage,
    StatisticsConfigurationHistory configuration,
    StatisticsRecomputeSignal signal,
    ILogger<InstallationFacilityChangedHandler> logger) : IConsumer<InstallationFacilityChangedV1>
{
    public async Task OnHandle(InstallationFacilityChangedV1 message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("OnHandle.InstallationFacilityChanged");
        activity?.SetTag("InstallationId", message.InstallationId);
        activity?.SetTag("EventId", message.EventId);
        if (message.EventId == Guid.Empty || message.InstallationId == Guid.Empty)
            throw new ArgumentException("InstallationFacilityChangedV1 requires nonempty event and installation IDs.", nameof(message));

        await configuration.EvictAsync([message.InstallationId], []);
        StatisticsRecomputeScheduler.Installation(storage, message.InstallationId, RecomputeReason.Configuration, message.EventId,
            Utc(message.FromUtc), Utc(message.ToUtc));
        await storage.SaveChangesAsync(cancellationToken);
        storage.ChangeTracker.Clear();
        signal.Notify();
        logger.LogInformation("Installation {InstallationId} facility change {EventId} marked {From}..{To} dirty",
            message.InstallationId, message.EventId, message.FromUtc, message.ToUtc);
    }

    private static DateTime? Utc(DateTime? value) =>
        value is { } time ? DateTime.SpecifyKind(time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time, DateTimeKind.Utc) : null;
}
