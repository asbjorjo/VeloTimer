namespace VeloTime.Module.Facilities.Interface.Data;

/// <summary>
/// A layout as it is at one instant (the request's <c>at</c>, default now): the segments of the revision in
/// effect then, which are empty when no revision covers the instant.
/// </summary>
public class CourseLayoutDTO
{
    public required Guid Id { get; init; }
    public required Guid FacilityId { get; init; }
    public ICollection<SegmentDTO> Segments { get; set; } = new List<SegmentDTO>();
    /// <summary>The revision the segments come from, or null when none covers the instant.</summary>
    public Guid? RevisionId { get; set; }
    public DateTimeOffset? RevisionValidFromUtc { get; set; }
    public DateTimeOffset? RevisionValidToUtc { get; set; }
    /// <summary>Whether the facility runs this layout at the instant.</summary>
    public bool InUse { get; set; } = true;
}
