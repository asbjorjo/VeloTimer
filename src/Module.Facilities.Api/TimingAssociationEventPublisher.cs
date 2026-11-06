using Microsoft.EntityFrameworkCore;
using SlimMessageBus;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api;

/// <summary>Enqueues only; the caller owns SaveChanges, commit/rollback and post-commit cache invalidation.</summary>
public interface ITimingAssociationEventPublisher
{
    Task EnqueueAsync(CoursePointTimingAssociationsChangedV1 message, CancellationToken cancellationToken = default);
}

internal sealed class TimingAssociationEventPublisher(FacilityDbContext storage, IMessageBus bus)
    : ITimingAssociationEventPublisher
{
    public Task EnqueueAsync(CoursePointTimingAssociationsChangedV1 message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!storage.Database.IsNpgsql() || storage.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Facilities events require an active PostgreSQL FacilityDbContext transaction.");

        // Serialization and outbox insertion happen now on this scope's FacilityDbContext
        // connection/transaction. Never regenerate IDs or snapshots in a dispatch callback.
        return bus.Publish(Prepare(message), cancellationToken: cancellationToken);
    }

    internal static CoursePointTimingAssociationsChangedV1 Prepare(CoursePointTimingAssociationsChangedV1 message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.EventId == Guid.Empty || message.ChangeId == Guid.Empty || message.FacilityId == Guid.Empty)
            throw new ArgumentException("EventId, ChangeId and FacilityId must be non-empty.", nameof(message));
        if (message.Changes is null || message.Changes.Count == 0)
            throw new ArgumentException("An event must contain effective association deltas.", nameof(message));

        var changes = message.Changes.Select(change =>
        {
            if (change is null || (change.Before is null && change.After is null))
                throw new ArgumentException("An association delta must have a before or after snapshot.", nameof(message));
            return new CoursePointTimingAssociationChange(Normalize(change.Before), Normalize(change.After));
        }).ToArray();
        return message with { OccurredAtUtc = message.OccurredAtUtc.ToUniversalTime(), Changes = changes };
    }

    private static CoursePointTimingAssociationSnapshot? Normalize(CoursePointTimingAssociationSnapshot? snapshot)
    {
        if (snapshot is null) return null;
        if (snapshot.TimingPointId == Guid.Empty || snapshot.CoursePointId == Guid.Empty)
            throw new ArgumentException("Snapshot timing-point and coursepoint IDs must be non-empty.");
        return snapshot with
        {
            ValidFromUtc = snapshot.ValidFromUtc?.ToUniversalTime(),
            ValidToUtc = snapshot.ValidToUtc?.ToUniversalTime()
        };
    }
}
