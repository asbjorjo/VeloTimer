using Microsoft.EntityFrameworkCore;
using SlimMessageBus;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api;

/// <summary>Enqueues only; the caller owns SaveChanges, commit/rollback and post-commit cache invalidation.</summary>
public interface ICourseConfigEventPublisher
{
    Task EnqueueAsync(CourseConfigChangedV1 message, CancellationToken cancellationToken = default);
}

internal sealed class CourseConfigEventPublisher(FacilityDbContext storage, IMessageBus bus)
    : ICourseConfigEventPublisher
{
    public Task EnqueueAsync(CourseConfigChangedV1 message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!storage.Database.IsNpgsql() || storage.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Facilities events require an active PostgreSQL FacilityDbContext transaction.");

        // Serialization and outbox insertion happen now on this scope's FacilityDbContext
        // connection/transaction. Never regenerate IDs or snapshots in a dispatch callback.
        return bus.Publish(Prepare(message), cancellationToken: cancellationToken);
    }

    internal static CourseConfigChangedV1 Prepare(CourseConfigChangedV1 message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.EventId == Guid.Empty || message.ChangeId == Guid.Empty || message.FacilityId == Guid.Empty)
            throw new ArgumentException("EventId, ChangeId and FacilityId must be non-empty.", nameof(message));
        if (message.AssociationChanges is null)
            throw new ArgumentException("AssociationChanges must not be null.", nameof(message));
        if (message.Kind == CourseConfigChangeKind.TimingAssociation && message.AssociationChanges.Count == 0)
            throw new ArgumentException("A timing-association event must contain effective association deltas.", nameof(message));
        if (message.Kind != CourseConfigChangeKind.TimingAssociation && (message.CourseLayoutId is null || message.CourseLayoutId == Guid.Empty))
            throw new ArgumentException("A layout event must name its course layout.", nameof(message));
        if (message.AffectedFromUtc is { } from && message.AffectedToUtc is { } to && from >= to)
            throw new ArgumentException("The affected interval must not be empty.", nameof(message));

        var changes = message.AssociationChanges.Select(change =>
        {
            if (change is null || (change.Before is null && change.After is null))
                throw new ArgumentException("An association delta must have a before or after snapshot.", nameof(message));
            return new CoursePointTimingAssociationChange(Normalize(change.Before), Normalize(change.After));
        }).ToArray();
        return message with
        {
            OccurredAtUtc = message.OccurredAtUtc.ToUniversalTime(),
            AffectedFromUtc = message.AffectedFromUtc?.ToUniversalTime(),
            AffectedToUtc = message.AffectedToUtc?.ToUniversalTime(),
            AssociationChanges = changes
        };
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
