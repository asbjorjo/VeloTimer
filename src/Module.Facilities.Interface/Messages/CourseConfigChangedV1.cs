using System.Text.Json.Serialization;
using VeloTime.Module.Facilities.Interface.Data;

namespace VeloTime.Module.Facilities.Interface.Messages;

/// <summary>What a <see cref="CourseConfigChangedV1"/> edit changed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CourseConfigChangeKind>))]
public enum CourseConfigChangeKind
{
    /// <summary>A timing point ↔ course point association; <see cref="CourseConfigChangedV1.AssociationChanges"/> has the deltas.</summary>
    TimingAssociation,
    /// <summary>When the facility runs a layout (a <c>FacilityLayoutAssignment</c>).</summary>
    LayoutAssignment,
    /// <summary>A layout's geometry from an effective date (a <c>CourseLayoutRevision</c>).</summary>
    LayoutRevision,
    /// <summary>A layout and its whole history were deleted.</summary>
    LayoutDeleted
}

/// <summary>
/// Version 1 of the Facilities course configuration change event (docs/replay-safe-statistics-plan.md §4.1):
/// one event per atomic edit of a facility's time-bound course configuration. It carries the affected interval
/// [AffectedFromUtc, AffectedToUtc) and scope, so consumers evict the facility's cached history and recompute
/// results with events in that interval. Null bounds are unbounded. Timestamps are UTC with a zero offset.
/// </summary>
public sealed record CourseConfigChangedV1(
    Guid EventId,
    Guid ChangeId,
    DateTimeOffset OccurredAtUtc,
    Guid FacilityId,
    CourseConfigChangeKind Kind,
    DateTimeOffset? AffectedFromUtc,
    DateTimeOffset? AffectedToUtc,
    Guid? CourseLayoutId,
    IReadOnlyList<CoursePointTimingAssociationChange> AssociationChanges)
{
    /// <summary>The smallest interval containing every association delta's before and after intervals.</summary>
    public static (DateTimeOffset? From, DateTimeOffset? To) Hull(IEnumerable<CoursePointTimingAssociationChange> changes)
    {
        var any = false;
        DateTimeOffset? from = null, to = null;
        var unboundedFrom = false;
        var unboundedTo = false;
        foreach (var snapshot in changes.SelectMany(change => new[] { change.Before, change.After }).OfType<CoursePointTimingAssociationSnapshot>())
        {
            any = true;
            if (snapshot.ValidFromUtc is not { } lower) unboundedFrom = true;
            else if (from is null || lower < from) from = lower;
            if (snapshot.ValidToUtc is not { } upper) unboundedTo = true;
            else if (to is null || upper > to) to = upper;
        }
        if (!any) return (null, null);
        return (unboundedFrom ? null : from, unboundedTo ? null : to);
    }
}
