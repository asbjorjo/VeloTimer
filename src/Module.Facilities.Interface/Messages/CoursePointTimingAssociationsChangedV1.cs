using VeloTime.Module.Facilities.Interface.Data;

namespace VeloTime.Module.Facilities.Interface.Messages;

/// <summary>
/// Version 1 of the Facilities-owned effective timing-association change event.
/// Event timestamps are UTC DateTimeOffset instants serialized with a zero offset.
/// </summary>
public sealed record CoursePointTimingAssociationsChangedV1(
    Guid EventId,
    Guid ChangeId,
    DateTimeOffset OccurredAtUtc,
    Guid FacilityId,
    IReadOnlyList<CoursePointTimingAssociationChange> Changes);
