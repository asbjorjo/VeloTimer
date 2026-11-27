using SlimMessageBus;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Statistics.Handlers;

/// <summary>
/// Ownership is applied by the recompute worker (plan §5.5), so an ownership change is one more dirty-range
/// trigger: the cached ownership history is evicted first, then the transponder is marked dirty over every
/// changed period (evict-before-enqueue, plan §4.3). Redelivery only marks the same ranges dirty again.
/// </summary>
public sealed class TransponderOwnershipChangedHandler(
    StatisticsDbContext storage,
    StatisticsConfigurationHistory configuration,
    StatisticsRecomputeSignal signal,
    ILogger<TransponderOwnershipChangedHandler> logger) : IConsumer<TransponderOwnershipChanged>
{
    public async Task OnHandle(TransponderOwnershipChanged message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("OnHandle.TransponderOwnershipChanged");
        activity?.SetTag("TransponderId", message.TransponderId);
        activity?.SetTag("EventId", message.EventId);
        var ranges = message.Changes.SelectMany(change => new[] { change.Before, change.After })
            .OfType<Timing.Interface.Data.TransponderOwnershipDTO>().Distinct().ToList();
        if (message.TransponderId == Guid.Empty || ranges.Count == 0)
            throw new ArgumentException("Ownership changes must name a transponder and contain at least one period.", nameof(message));
        if (ranges.Any(range => range.TransponderId != message.TransponderId || range.OwnedTo.HasValue && range.OwnedTo <= range.OwnedFrom))
            throw new ArgumentException("Invalid transponder ownership period.", nameof(message));

        await configuration.EvictOwnershipAsync(message.TransponderId);
        foreach (var range in ranges)
            StatisticsRecomputeScheduler.Transponder(storage, message.TransponderId, range.OwnedFrom.UtcDateTime,
                range.OwnedTo?.UtcDateTime, RecomputeReason.Configuration, message.EventId);
        await storage.SaveChangesAsync(cancellationToken);
        storage.ChangeTracker.Clear();
        signal.Notify();
        logger.LogInformation("Ownership event {EventId} marked {Count} period(s) of transponder {TransponderId} dirty",
            message.EventId, ranges.Count, message.TransponderId);
    }
}
