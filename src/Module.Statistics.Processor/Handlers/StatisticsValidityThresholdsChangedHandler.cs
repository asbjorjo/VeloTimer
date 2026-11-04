using Microsoft.Extensions.Logging;
using SlimMessageBus;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Handlers;

/// <summary>
/// Wake-up for durable revalidation. The API already scheduled the job transactionally; this handler
/// never increments the generation or resets a cursor, so duplicate or out-of-order deliveries are
/// harmless. A missing job row (writer outside the protocol) is created pending.
/// </summary>
public class StatisticsValidityThresholdsChangedHandler(
    StatisticsDbContext storage,
    StatisticsValidityScheduler scheduler,
    StatisticsRevalidationSignal signal,
    ILogger<StatisticsValidityThresholdsChangedHandler> logger) : IConsumer<StatisticsValidityThresholdsChangedV1>
{
    public async Task OnHandle(StatisticsValidityThresholdsChangedV1 message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("OnHandle.StatisticsValidityThresholdsChanged");
        activity?.SetTag("StatisticsItemId", message.StatisticsItemId);
        if (message.EventId == Guid.Empty || message.StatisticsItemId == Guid.Empty)
            throw new ArgumentException("Threshold change events require nonempty EventId and StatisticsItemId.", nameof(message));

        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        var locked = await scheduler.LockItemsAsync([message.StatisticsItemId], StatisticsItemLockMode.Shared, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (locked.Count == 0)
        {
            logger.LogInformation(
                "Threshold change {EventId} refers to statistics item {StatisticsItemId}, which no longer exists; nothing to revalidate",
                message.EventId, message.StatisticsItemId);
            return;
        }
        logger.LogInformation("Threshold change {EventId} for statistics item {StatisticsItemId} (configuration {ConfigId}) woke revalidation",
            message.EventId, message.StatisticsItemId, message.StatisticsItemConfigId);
        signal.Notify();
    }
}
