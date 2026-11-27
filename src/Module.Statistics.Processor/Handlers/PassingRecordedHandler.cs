using Microsoft.EntityFrameworkCore;
using SlimMessageBus;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Statistics.Handlers;

/// <summary>
/// Stores Timing's passing fact idempotently on <see cref="PassingRecordedV1.PassingId"/> and marks its
/// instant dirty in the same transaction (plan §3.1, §3.3). A redelivery inserts no fact and only marks the
/// same range dirty again, which is harmless. Derivation happens in the recompute worker.
/// </summary>
public sealed class PassingRecordedHandler(
    StatisticsDbContext storage,
    StatisticsRecomputeSignal signal,
    Metrics metrics,
    ILogger<PassingRecordedHandler> logger,
    TimeProvider? timeProvider = null) : IConsumer<PassingRecordedV1>
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public async Task OnHandle(PassingRecordedV1 message, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("OnHandle.PassingRecorded");
        activity?.SetTag("PassingId", message.PassingId);
        activity?.SetTag("TransponderId", message.TransponderId);
        if (message.PassingId == Guid.Empty || message.TransponderId == Guid.Empty || message.TimingPointId == Guid.Empty
            || message.InstallationId == Guid.Empty)
            throw new ArgumentException("PassingRecordedV1 requires nonempty passing, installation, timing point and transponder IDs.", nameof(message));

        var instant = DateTime.SpecifyKind(message.Time.Kind == DateTimeKind.Local ? message.Time.ToUniversalTime() : message.Time,
            DateTimeKind.Utc);
        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        var inserted = await storage.Database.ExecuteSqlAsync($"""
            INSERT INTO statistics.passing_fact
                (passing_id, installation_id, timing_point_id, transponder_id, time, low_battery, low_strength, low_hits, recorded_at_utc)
            VALUES ({message.PassingId}, {message.InstallationId}, {message.TimingPointId}, {message.TransponderId}, {instant},
                {message.LowBattery}, {message.LowStrength}, {message.LowHits}, {now})
            ON CONFLICT (passing_id) DO NOTHING
            """, cancellationToken) == 1;
        StatisticsRecomputeScheduler.Transponder(storage, message.TransponderId, instant, instant, RecomputeReason.Passing, message.PassingId);
        await storage.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        storage.ChangeTracker.Clear();
        signal.Notify();

        metrics.PassingRecorded(inserted);
        activity?.SetTag("Inserted", inserted);
        if (!inserted)
            logger.LogDebug("Passing {PassingId} was already recorded; marked its range dirty again", message.PassingId);
    }
}
