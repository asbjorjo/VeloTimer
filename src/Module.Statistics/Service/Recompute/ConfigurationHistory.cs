namespace VeloTime.Module.Statistics.Service.Recompute;

// Cached through HybridCache (System.Text.Json with reference preservation), so these are plain
// classes with settable properties rather than positional records.

/// <summary>A course segment as Facilities stores it today (Phase 3 makes layouts dated revisions).</summary>
public sealed class CourseSegmentVersion
{
    public int Order { get; init; }
    public Guid StartPointId { get; init; }
    public Guid EndPointId { get; init; }
    public double Length { get; init; }
}

public sealed class CourseLayoutVersion
{
    public Guid LayoutId { get; init; }
    public List<CourseSegmentVersion> Segments { get; init; } = [];
}

/// <summary>A timing point ↔ course point association over [ValidFromUtc, ValidToUtc); null is unbounded.</summary>
public sealed class TimingAssociationVersion
{
    public Guid TimingPointId { get; init; }
    public Guid CoursePointId { get; init; }
    public DateTime? ValidFromUtc { get; init; }
    public DateTime? ValidToUtc { get; init; }

    public bool Contains(DateTime instant) =>
        (ValidFromUtc is not { } from || from <= instant) && (ValidToUtc is not { } to || instant < to);
}

/// <summary>
/// One facility's whole course configuration history, cached as one entry (plan §4.3) so a recompute
/// resolves every instant locally. Associations are already time-bound; layouts are current until Phase 3.
/// </summary>
public sealed class FacilityCourseHistory
{
    public Guid FacilityId { get; init; }
    public List<CourseLayoutVersion> Layouts { get; init; } = [];
    public List<TimingAssociationVersion> Associations { get; init; } = [];

    /// <summary>The course point a timing point is associated with at <paramref name="instant"/>, if exactly one.</summary>
    public Guid? ResolveCoursePoint(Guid timingPointId, DateTime instant)
    {
        Guid? found = null;
        foreach (var association in Associations)
        {
            if (association.TimingPointId != timingPointId || !association.Contains(instant)) continue;
            if (found.HasValue && found != association.CoursePointId) return null;
            found = association.CoursePointId;
        }
        return found;
    }

    /// <summary>Distance between two course points; see <see cref="CourseGeometry.Resolve"/>.</summary>
    public CourseDistance? ResolveDistance(Guid start, Guid end) => CourseGeometry.Resolve(Layouts, start, end);
}

/// <summary>An installation's facility. Phase 4 makes this a dated assignment history.</summary>
public sealed class InstallationFacilityHistory
{
    public Guid InstallationId { get; init; }
    public Guid? FacilityId { get; init; }

    public Guid? ResolveFacility(DateTime instant) => FacilityId;
}

/// <summary>Endpoint distance plus closed-loop evidence, as Facilities resolves it.</summary>
public sealed record CourseDistance(double Distance, Guid? CourseLayoutId, double? ClosedLoopDistanceMetres);
