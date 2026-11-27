using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace VeloTime.Module.Common.Operations;

/// <summary>
/// Gauges for message delivery health (plan Phase 5): transactional outbox backlog and Service Bus dead-letter
/// queues. Monitors push samples here; the gauges report the latest sample per outbox or subscription and
/// nothing before the first. Meter <see cref="MeterName"/>.
/// </summary>
public sealed class MessagingMetrics
{
    public const string MeterName = "VeloTime.Messaging";

    private readonly ConcurrentDictionary<string, OutboxSample> outboxes = new();
    private readonly ConcurrentDictionary<(string Topic, string Subscription), DeadLetterSample> deadLetters = new();
    private readonly Counter<long> deadLettered;
    private readonly Counter<long> sampleFailures;

    public MessagingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        meter.CreateObservableGauge("velotime.messaging.outbox.pending",
            () => outboxes.Select(o => new Measurement<long>(o.Value.Pending, Outbox(o.Key))),
            unit: "{message}", description: "Outbox messages not yet delivered (excludes aborted)");
        meter.CreateObservableGauge("velotime.messaging.outbox.oldest_age",
            () => outboxes.Select(o => new Measurement<double>(Age(o.Value.Oldest, o.Value.ObservedAtUtc), Outbox(o.Key))),
            unit: "s", description: "Age of the oldest undelivered outbox message; 0 when the outbox is drained");
        meter.CreateObservableGauge("velotime.messaging.outbox.aborted",
            () => outboxes.Select(o => new Measurement<long>(o.Value.Aborted, Outbox(o.Key))),
            unit: "{message}", description: "Outbox messages whose delivery was aborted and that will never be sent");
        meter.CreateObservableGauge("velotime.messaging.dead_letter.messages",
            () => deadLetters.Select(d => new Measurement<long>(d.Value.Count, Subscription(d.Key))),
            unit: "{message}", description: "Messages in a subscription's dead-letter queue (capped at the monitor's peek limit)");
        meter.CreateObservableGauge("velotime.messaging.dead_letter.oldest_age",
            () => deadLetters.Select(d => new Measurement<double>(Age(d.Value.Oldest, d.Value.ObservedAtUtc), Subscription(d.Key))),
            unit: "s", description: "Age of the oldest dead-lettered message; 0 when the dead-letter queue is empty");
        deadLettered = meter.CreateCounter<long>("velotime.messaging.dead_letter.new",
            unit: "{message}", description: "Dead-lettered messages first seen by the monitor");
        sampleFailures = meter.CreateCounter<long>("velotime.messaging.monitor.failures",
            description: "Failed outbox or dead-letter samples, by monitor");
    }

    public void ObserveOutbox(string outbox, long pending, long aborted, DateTime? oldestUtc, DateTime observedAtUtc) =>
        outboxes[outbox] = new(pending, aborted, oldestUtc, observedAtUtc);

    public void ObserveDeadLetters(string topic, string subscription, long count, DateTime? oldestUtc, DateTime observedAtUtc) =>
        deadLetters[(topic, subscription)] = new(count, oldestUtc, observedAtUtc);

    public void DeadLettered(string topic, string subscription, long count)
    {
        if (count > 0) deadLettered.Add(count, Subscription((topic, subscription)));
    }

    public void SampleFailed(string monitor) =>
        sampleFailures.Add(1, new KeyValuePair<string, object?>("monitor", monitor));

    private static double Age(DateTime? oldest, DateTime observedAtUtc) =>
        oldest is { } at ? Math.Max(0, (observedAtUtc - at).TotalSeconds) : 0;

    private static KeyValuePair<string, object?>[] Outbox(string name) => [new("outbox", name)];

    private static KeyValuePair<string, object?>[] Subscription((string Topic, string Subscription) key) =>
        [new("topic", key.Topic), new("subscription", key.Subscription)];

    private sealed record OutboxSample(long Pending, long Aborted, DateTime? Oldest, DateTime ObservedAtUtc);
    private sealed record DeadLetterSample(long Count, DateTime? Oldest, DateTime ObservedAtUtc);
}
