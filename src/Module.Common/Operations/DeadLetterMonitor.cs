using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VeloTime.Module.Common.Operations;

/// <summary>A Service Bus subscription whose dead-letter queue a host watches.</summary>
public sealed record DeadLetterSubscription(string Topic, string Subscription);

public sealed class DeadLetterMonitorOptions
{
    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Most messages peeked per sample; the count gauge stops at this value.</summary>
    public int PeekLimit { get; set; } = 1000;
    /// <summary>Most newly dead-lettered messages logged one by one per sample; the rest are summarised.</summary>
    public int LogLimit { get; set; } = 20;
    public IReadOnlyList<DeadLetterSubscription> Subscriptions { get; set; } = [];
}

/// <summary>One dead-lettered message as the monitor sees it (peeked, never received).</summary>
public sealed record DeadLetteredMessage(long SequenceNumber, string? MessageId, string? MessageName, string? SessionId,
    DateTime EnqueuedAtUtc, string? Reason, string? Description, int DeliveryCount);

/// <summary>
/// Remembers the highest dead-letter sequence number seen per subscription, so each dead-lettered message is
/// alerted once per process. Pure; the monitor feeds it peeked batches.
/// </summary>
public sealed class DeadLetterTracker
{
    private readonly Dictionary<DeadLetterSubscription, long> highest = new();

    /// <summary>The messages in <paramref name="peeked"/> not seen in earlier samples, oldest first.</summary>
    public IReadOnlyList<DeadLetteredMessage> NewSince(DeadLetterSubscription subscription, IReadOnlyList<DeadLetteredMessage> peeked)
    {
        var seen = highest.TryGetValue(subscription, out var value) ? value : long.MinValue;
        var fresh = peeked.Where(m => m.SequenceNumber > seen).OrderBy(m => m.SequenceNumber).ToList();
        if (fresh.Count > 0) highest[subscription] = fresh[^1].SequenceNumber;
        return fresh;
    }
}

/// <summary>
/// Watches dead-letter queues (plan Phase 5 "DLQ alerts"). Each sample peeks, without receiving, up to
/// <see cref="DeadLetterMonitorOptions.PeekLimit"/> messages per subscription; it updates the dead-letter gauges
/// and logs every message dead-lettered since the previous sample at Error level, which is the alert signal for
/// log-based alerting. Peeking needs only Listen rights, so it runs with the host's runtime connection.
/// </summary>
public sealed class DeadLetterMonitor(
    ServiceBusClient client,
    MessagingMetrics metrics,
    DeadLetterMonitorOptions options,
    ILogger<DeadLetterMonitor> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private const int PeekBatch = 100;
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly DeadLetterTracker tracker = new();

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        options.Subscriptions.Count == 0 ? Task.CompletedTask
            : MonitorLoop.RunAsync(options.SampleInterval, time, SampleAsync, exception =>
            {
                metrics.SampleFailed("dead-letter");
                logger.LogWarning(exception, "Dead-letter sample failed");
            }, stoppingToken);

    public override void Dispose()
    {
        base.Dispose();
        _ = client.DisposeAsync().AsTask();
    }

    public async Task SampleAsync(CancellationToken cancellationToken)
    {
        foreach (var subscription in options.Subscriptions)
        {
            var peeked = await PeekAsync(subscription, cancellationToken);
            Report(subscription, peeked);
        }
    }

    /// <summary>Updates the gauges and alerts on new dead letters for one subscription's peeked queue.</summary>
    public void Report(DeadLetterSubscription subscription, IReadOnlyList<DeadLetteredMessage> peeked)
    {
        var now = time.GetUtcNow().UtcDateTime;
        metrics.ObserveDeadLetters(subscription.Topic, subscription.Subscription, peeked.Count,
            peeked.Count == 0 ? null : peeked.Min(m => m.EnqueuedAtUtc), now);
        var fresh = tracker.NewSince(subscription, peeked);
        metrics.DeadLettered(subscription.Topic, subscription.Subscription, fresh.Count);
        foreach (var message in fresh.Take(options.LogLimit))
            logger.LogError(
                "Message dead-lettered on {Topic}/{Subscription}: {MessageName} {MessageId} (session {SessionId}, sequence {SequenceNumber}, " +
                "{DeliveryCount} deliveries, enqueued {EnqueuedAtUtc:o}). Reason: {DeadLetterReason}. {DeadLetterDescription}",
                subscription.Topic, subscription.Subscription, message.MessageName, message.MessageId, message.SessionId,
                message.SequenceNumber, message.DeliveryCount, message.EnqueuedAtUtc, message.Reason, message.Description);
        if (fresh.Count > options.LogLimit)
            logger.LogError("{Count} more messages dead-lettered on {Topic}/{Subscription} since the last sample",
                fresh.Count - options.LogLimit, subscription.Topic, subscription.Subscription);
    }

    private async Task<IReadOnlyList<DeadLetteredMessage>> PeekAsync(DeadLetterSubscription subscription, CancellationToken cancellationToken)
    {
        await using var receiver = client.CreateReceiver(subscription.Topic, subscription.Subscription,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter, ReceiveMode = ServiceBusReceiveMode.PeekLock });
        var messages = new List<DeadLetteredMessage>();
        long next = 0;
        while (messages.Count < options.PeekLimit)
        {
            var batch = await receiver.PeekMessagesAsync(Math.Min(PeekBatch, options.PeekLimit - messages.Count), next, cancellationToken);
            if (batch.Count == 0) break;
            messages.AddRange(batch.Select(m => new DeadLetteredMessage(m.SequenceNumber, m.MessageId,
                m.ApplicationProperties.TryGetValue("MessageName", out var name) ? name?.ToString() : m.Subject,
                m.SessionId, m.EnqueuedTime.UtcDateTime, m.DeadLetterReason, m.DeadLetterErrorDescription, m.DeliveryCount)));
            next = batch[^1].SequenceNumber + 1;
        }
        return messages;
    }
}
