using SlimMessageBus;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Statistics.Handlers;

public sealed class TransponderOwnershipChangedHandler(
    StatisticsOwnershipService ownership,
    ILogger<TransponderOwnershipChangedHandler> logger) : IConsumer<TransponderOwnershipChanged>
{
    public async Task OnHandle(TransponderOwnershipChanged message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("OnHandle.TransponderOwnershipChanged");
        activity?.SetTag("TransponderId", message.TransponderId);
        activity?.SetTag("EventId", message.EventId);
        var updated = await ownership.ReconcileAsync(message, cancellationToken);
        activity?.SetTag("EntriesUpdated", updated);
        logger.LogInformation("Reconciled ownership event {EventId} for transponder {TransponderId}; updated {EntriesUpdated} statistics entries",
            message.EventId, message.TransponderId, updated);
    }
}
