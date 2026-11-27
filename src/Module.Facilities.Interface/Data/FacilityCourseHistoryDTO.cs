namespace VeloTime.Module.Facilities.Interface.Data;

/// <summary>
/// A facility's complete course configuration history in one read, for consumers that resolve
/// configuration at event time (Statistics recompute, docs/replay-safe-statistics-plan.md §4.3).
/// Associations carry their full effective intervals. Layouts are current until they get dated revisions.
/// </summary>
public sealed class FacilityCourseHistoryDTO
{
    public required Guid FacilityId { get; init; }
    public required IReadOnlyList<CourseLayoutHistoryDTO> Layouts { get; init; }
    public required IReadOnlyList<CoursePointTimingAssociationSnapshot> Associations { get; init; }
}

public sealed class CourseLayoutHistoryDTO
{
    public required Guid LayoutId { get; init; }
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
