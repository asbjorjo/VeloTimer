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
    ) : IConsumer<SampleComplete>, IConsumerWithContext
{
    public IConsumerContext Context { get; set; } = null!;
    public async Task OnHandle(SampleComplete message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("OnHandle.SampleComplete");
        activity?.SetTag("TransponderId", message.TransponderId);

        var entries = await ownership.CreateEntriesFromSourceAsync(message.TransponderId,
            (sourceId, cancel) => statistics.ProcessCompletedAsync(message.TransponderId, message.TimeStart, message.TimeEnd,
                message.CoursePointStart, message.CoursePointEnd, cancel, sourceId, message.Distance), cancellationToken,
            enqueueSingle: true, completion: message,
            headers: Context?.Headers ?? throw new InvalidOperationException("SampleComplete consumer context is required."));
        activity?.SetTag("EntriesCreated", entries.Count());
        logger.LogInformation("Processed SampleComplete for TransponderId: {TransponderId}, Entries Created: {EntryCount}",
            message.TransponderId, entries.Count());

        metrics.SampleCompleted((DateTime.UtcNow - activity?.StartTimeUtc)?.TotalMilliseconds ?? 0);

        activity?.AddEvent(new("EntriesEnqueued"));
    }
}
