using Microsoft.EntityFrameworkCore;
using SlimMessageBus;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api;

/// <summary>Enqueue on the caller's scoped Statistics transaction; never dispatch after commit.</summary>
public interface IStatisticsThresholdEnqueue
{
    Task EnqueueAsync(StatisticsValidityThresholdsChangedV1 message, CancellationToken cancellationToken = default);
}

/// <summary>Requires the host's producer to use AddStatisticsOutbox with the same scoped DbContext.</summary>
public sealed class StatisticsThresholdEnqueue(StatisticsDbContext storage, IMessageBus bus) : IStatisticsThresholdEnqueue
{
    public Task EnqueueAsync(StatisticsValidityThresholdsChangedV1 message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        if (message.EventId == Guid.Empty || message.StatisticsItemId == Guid.Empty ||
            message.StatisticsItemConfigId == Guid.Empty)
            throw new ArgumentException("Event and scope IDs must be nonempty.", nameof(message));
        if (!storage.Database.IsNpgsql() || storage.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Threshold events require an active PostgreSQL StatisticsDbContext transaction.");

        return bus.Publish(message, cancellationToken: cancellationToken);
    }
}
