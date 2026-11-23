using Microsoft.Extensions.Logging;
using SlimMessageBus;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Module.Statistics.Handlers;

public class SampleCompleteHandler(
    StatisticsService statistics,
    StatisticsOwnershipService ownership,
    Metrics metrics,
    ILogger<SampleCompleteHandler> logger
    ) : IConsumer<SampleComplete>
{
    public async Task OnHandle(SampleComplete message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("OnHandle.SampleComplete");
        activity?.SetTag("TransponderId", message.TransponderId);

        var entries = await ownership.CreateEntriesAsync(message.TransponderId,
            cancel => statistics.ProcessCompletedAsync(message.TransponderId, message.TimeStart, message.TimeEnd,
                message.CoursePointStart, message.CoursePointEnd, cancel), cancellationToken,
            enqueueSingle: true, completion: message);
        activity?.SetTag("EntriesCreated", entries.Count());
        logger.LogInformation("Processed SampleComplete for TransponderId: {TransponderId}, Entries Created: {EntryCount}",
            message.TransponderId, entries.Count());

        metrics.SampleCompleted((DateTime.UtcNow - activity?.StartTimeUtc)?.TotalMilliseconds ?? 0);

        activity?.AddEvent(new("EntriesEnqueued"));
    }
}
