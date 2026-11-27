using System.Diagnostics.Metrics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Common.Operations;

namespace VeloTime.Workers.Test;

/// <summary>Dead-letter alerting (replay-safe plan Phase 5) without a Service Bus namespace.</summary>
public sealed class DeadLetterMonitorTests
{
    private static readonly DeadLetterSubscription Statistics = new("velotime-timing", "statistics");
    private static readonly DateTime Now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TrackerReportsEachDeadLetterOncePerSubscription()
    {
        var tracker = new DeadLetterTracker();
        var other = new DeadLetterSubscription("velotime-facilities", "statistics");

        Assert.Equal([1L, 2L], tracker.NewSince(Statistics, [Message(2), Message(1)]).Select(m => m.SequenceNumber));
        Assert.Empty(tracker.NewSince(Statistics, [Message(1), Message(2)]));
        Assert.Equal([3L], tracker.NewSince(Statistics, [Message(2), Message(3)]).Select(m => m.SequenceNumber));
        // A drained queue, then a new dead letter, still alerts.
        Assert.Empty(tracker.NewSince(Statistics, []));
        Assert.Equal([4L], tracker.NewSince(Statistics, [Message(4)]).Select(m => m.SequenceNumber));
        Assert.Equal([1L], tracker.NewSince(other, [Message(1)]).Select(m => m.SequenceNumber));
    }

    [Fact]
    public async Task ReportUpdatesGaugesAndLogsNewDeadLettersAtErrorUpToTheLimit()
    {
        await using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new MessagingMetrics(services.GetRequiredService<IMeterFactory>());
        var logger = new ListLogger();
        await using var client = new ServiceBusClient("Endpoint=sb://unused.invalid/;SharedAccessKeyName=k;SharedAccessKey=a2V5");
        using var monitor = new DeadLetterMonitor(client, metrics,
            new DeadLetterMonitorOptions { LogLimit = 2, Subscriptions = [Statistics] }, logger, new FixedTime(Now));
        using var gauges = new GaugeReader(MessagingMetrics.MeterName);

        monitor.Report(Statistics, [Message(1, Now.AddMinutes(-30)), Message(2), Message(3)]);

        var count = Assert.Single(gauges.Read("velotime.messaging.dead_letter.messages"));
        Assert.Equal((3d, "velotime-timing", "statistics"), (count.Value, count.Tags["topic"], count.Tags["subscription"]));
        Assert.Equal(1800, Assert.Single(gauges.Read("velotime.messaging.dead_letter.oldest_age")).Value, 3);
        Assert.Equal(3, logger.Errors.Count);
        Assert.Contains("PassingRecordedV1", logger.Errors[0]);
        Assert.Contains("MaxDeliveryCountExceeded", logger.Errors[0]);
        Assert.Contains("1 more messages", logger.Errors[2]);

        monitor.Report(Statistics, [Message(1, Now.AddMinutes(-30)), Message(2), Message(3)]);
        Assert.Equal(3, logger.Errors.Count);

        monitor.Report(Statistics, []);
        Assert.Equal(0, Assert.Single(gauges.Read("velotime.messaging.dead_letter.messages")).Value);
        Assert.Equal(0, Assert.Single(gauges.Read("velotime.messaging.dead_letter.oldest_age")).Value);
    }

    private static DeadLetteredMessage Message(long sequence, DateTime? enqueued = null) =>
        new(sequence, $"m{sequence}", "PassingRecordedV1", "session", enqueued ?? Now.AddMinutes(-1),
            "MaxDeliveryCountExceeded", "Message could not be consumed after 10 delivery attempts.", 10);

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class ListLogger : ILogger<DeadLetterMonitor>
    {
        public List<string> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error) Errors.Add(formatter(state, exception));
        }
    }
}
