namespace VeloTime.Module.Facilities.Interface.Data;

/// <summary>
/// A facility's complete course configuration history in one read, for consumers that resolve
/// configuration at event time (Statistics recompute, docs/replay-safe-statistics-plan.md §4.3).
/// Every item carries its full effective interval [ValidFromUtc, ValidToUtc); null bounds are unbounded.
/// </summary>
public sealed class FacilityCourseHistoryDTO
{
    public required Guid FacilityId { get; init; }
    public required IReadOnlyList<CourseLayoutHistoryDTO> Layouts { get; init; }
    public required IReadOnlyList<CoursePointTimingAssociationSnapshot> Associations { get; init; }
}

/// <summary>One layout: when the facility runs it, and its geometry revisions.</summary>
public sealed class CourseLayoutHistoryDTO
{
    public required Guid LayoutId { get; init; }
    /// <summary>When the facility runs this layout, in start order. Never overlapping.</summary>
    public required IReadOnlyList<EffectiveIntervalDTO> Assignments { get; init; }
    /// <summary>The layout's geometry over time, in start order. Never overlapping.</summary>
    public required IReadOnlyList<CourseLayoutRevisionHistoryDTO> Revisions { get; init; }
}

public sealed class CourseLayoutRevisionHistoryDTO
{
    public required Guid RevisionId { get; init; }
    public required DateTimeOffset? ValidFromUtc { get; init; }
    public required DateTimeOffset? ValidToUtc { get; init; }
    public required IReadOnlyList<CourseSegmentHistoryDTO> Segments { get; init; }
}

public sealed class CourseSegmentHistoryDTO
{
    public required int Order { get; init; }
    public required Guid StartPointId { get; init; }
    public required Guid EndPointId { get; init; }
    /// <summary>Segment length in metres.</summary>
    public required double Length { get; init; }
}

public sealed class EffectiveIntervalDTO
{
    public required DateTimeOffset? ValidFromUtc { get; init; }
    public required DateTimeOffset? ValidToUtc { get; init; }
}
