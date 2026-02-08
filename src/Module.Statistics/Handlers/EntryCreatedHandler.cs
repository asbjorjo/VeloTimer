using Microsoft.Extensions.Logging;
using SlimMessageBus;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Module.Statistics.Handlers;

public class EntryCreatedHandler(
    StatisticsService statistics,
    IMessageBus messageBus,
    ILogger<EntryCreatedHandler> logger) : IConsumer<EntryCreated>
{
    public async Task OnHandle(EntryCreated message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("OnHandle.EntryCreated");
        var entries = await statistics.ProcessEntryAsync(message.TransponderId, message.StatisticsItemId, message.ConfigItemId, message.TimeStart, message.TimeEnd);

        logger.LogInformation("Processed {EntriesCount} statistics entries for transponder {TransponderId}",
            entries.Count(), message.TransponderId);

        var entryMessages = entries.Select(e => new EntryCreated
        (
            TransponderId: e.TransponderId,
            TimeStart: e.TimeStart,
            TimeEnd: e.TimeEnd,
            StatisticsItemId: e.StatisticsItemId,
            ConfigItemId: e.StatisticsItemConfigId
        ));
    
        activity?.AddEvent(new("EntriesPublished"));
        await messageBus.Publish(entryMessages);
    }
}
