using SlimMessageBus;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Handlers;

/// <summary>
/// A facility's course configuration changed over an interval (plan §3.3, §4.3). Evicts the facility's cached
/// course history strictly first, and only then marks the interval dirty, so no recompute this change triggers
/// can read the history from before it. When eviction cannot be confirmed the handler throws and the message is
/// retried. A redelivery evicts and marks the same range dirty again, which is harmless.
/// </summary>
public sealed class CourseConfigChangedHandler(
    StatisticsDbContext storage,
    StatisticsConfigurationHistory configuration,
    StatisticsRecomputeSignal signal,
    ILogger<CourseConfigChangedHandler> logger) : IConsumer<CourseConfigChangedV1>
{
    public async Task OnHandle(CourseConfigChangedV1 message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("OnHandle.CourseConfigChanged");
        activity?.SetTag("FacilityId", message.FacilityId);
        activity?.SetTag("ChangeId", message.ChangeId);
        activity?.SetTag("Kind", message.Kind.ToString());
        if (message.EventId == Guid.Empty || message.ChangeId == Guid.Empty || message.FacilityId == Guid.Empty)
            throw new ArgumentException("CourseConfigChangedV1 requires nonempty EventId, ChangeId and FacilityId.", nameof(message));

        await configuration.EvictAsync([], [message.FacilityId]);

        // Facts carry their installation, not their facility, so the range is marked dirty for every transponder;
        // transponders whose results do not change are left untouched by the diff.
        var from = message.AffectedFromUtc?.UtcDateTime;
        var to = message.AffectedToUtc?.UtcDateTime;
        StatisticsRecomputeScheduler.All(storage, RecomputeReason.Configuration, message.ChangeId, from, to);
        await storage.SaveChangesAsync(cancellationToken);
        storage.ChangeTracker.Clear();
        signal.Notify();

        logger.LogInformation("Course configuration change {ChangeId} ({Kind}) at facility {FacilityId} marked {From}..{To} dirty",
            message.ChangeId, message.Kind, message.FacilityId, from?.ToString("o") ?? "-inf", to?.ToString("o") ?? "+inf");
    }
}
