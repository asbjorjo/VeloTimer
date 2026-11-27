namespace VeloTime.Module.Statistics.Model;

/// <summary>
/// A passing fact the last recompute could not place (plan §5.3): it has no facility or no course point at its
/// time. The worker keeps this set current for each cluster it recomputes, so the table is the live
/// "unresolved passings" backlog. Adding or backdating the missing configuration recomputes the range and
/// removes the row.
/// </summary>
public class UnresolvedPassing
{
    public Guid PassingId { get; init; }
    public Guid TransponderId { get; init; }
    public Guid InstallationId { get; init; }
    public Guid TimingPointId { get; init; }
    public DateTime Time { get; init; }
    public UnresolvedPassingReason Reason { get; set; }
    /// <summary>Facility the installation was assigned to at the passing's time, when that much resolved.</summary>
    public Guid? FacilityId { get; set; }
    public DateTime DetectedAtUtc { get; init; }
}

public enum UnresolvedPassingReason
{
    /// <summary>The installation had no facility assignment at the passing's time.</summary>
    NoFacility = 0,
    /// <summary>The timing point had no course point association at the passing's time.</summary>
    NoCoursePoint = 1
}
