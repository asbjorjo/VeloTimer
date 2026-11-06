using Microsoft.Extensions.Logging;
using SlimMessageBus;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Module.Statistics.Handlers;

public class SampleCompleteHandler(
    StatisticsService statistics,
    StatisticsOwnershipService ownership,
    Metrics metrics,
    IMessageBus messageBus,
    ILogger<SampleCompleteHandler> logger
    ) : IConsumer<SampleComplete>
{
    public async Task OnHandle(SampleComplete message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("OnHandle.SampleComplete");
        activity?.SetTag("TransponderId", message.TransponderId);

        var entries = await ownership.CreateEntriesAsync(message.TransponderId,
            cancel => statistics.ProcessCompletedAsync(message.TransponderId, message.TimeStart, message.TimeEnd,
                message.CoursePointStart, message.CoursePointEnd, cancel), cancellationToken);
        activity?.SetTag("EntriesCreated", entries.Count());
        logger.LogInformation("Processed SampleComplete for TransponderId: {TransponderId}, Entries Created: {EntryCount}",
            message.TransponderId, entries.Count());

        var entryMessages = entries.Select(e => new EntryCreated
        (
            TransponderId: e.TransponderId,
            TimeStart: e.TimeStart,
            TimeEnd: e.TimeEnd,
            StatisticsItemId: e.StatisticsItemId,
            ConfigItemId: e.StatisticsItemConfigId
        ));
        metrics.SampleCompleted((DateTime.UtcNow - activity?.StartTimeUtc)?.TotalMilliseconds ?? 0);

        activity?.AddEvent(new("EntriesPublished"));
        await messageBus.Publish(entryMessages, headers: new Dictionary<string, object> { ["Single"] = true }, cancellationToken: cancellationToken);
    }
}
