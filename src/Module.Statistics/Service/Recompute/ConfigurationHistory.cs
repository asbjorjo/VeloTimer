using VeloTime.Module.Facilities.Interface.Course;

namespace VeloTime.Module.Statistics.Service.Recompute;

// Cached through HybridCache (System.Text.Json with reference preservation), so these are plain
// classes with settable properties rather than positional records.

/// <summary>A course segment of one layout revision. Length is in metres.</summary>
public sealed class CourseSegmentVersion
{
    public int Order { get; init; }
    public Guid StartPointId { get; init; }
    public Guid EndPointId { get; init; }
    public double Length { get; init; }
}

/// <summary>An interval [ValidFromUtc, ValidToUtc); null is unbounded.</summary>
public class EffectiveVersion
{
    public DateTime? ValidFromUtc { get; init; }
    public DateTime? ValidToUtc { get; init; }

    public bool Contains(DateTime instant) =>
        (ValidFromUtc is not { } from || from <= instant) && (ValidToUtc is not { } to || instant < to);
}

/// <summary>A layout's geometry over its interval.</summary>
public sealed class CourseLayoutRevisionVersion : EffectiveVersion
{
    public Guid RevisionId { get; init; }
    public List<CourseSegmentVersion> Segments { get; init; } = [];
}

/// <summary>A layout: when the facility runs it (<see cref="Assignments"/>) and its geometry over time.</summary>
public sealed class CourseLayoutVersion
{
    public Guid LayoutId { get; init; }
    public List<EffectiveVersion> Assignments { get; init; } = [];
    public List<CourseLayoutRevisionVersion> Revisions { get; init; } = [];

    /// <summary>A layout in use for all time with one unbounded revision.</summary>
    public static CourseLayoutVersion Unbounded(Guid layoutId, List<CourseSegmentVersion> segments) => new()
    {
        LayoutId = layoutId, Assignments = [new()], Revisions = [new() { RevisionId = layoutId, Segments = segments }]
    };

    /// <summary>The layout's geometry at <paramref name="instant"/>, or null when it is not in use or has no revision then.</summary>
    public CourseLayoutGeometry? GeometryAt(DateTime instant)
    {
        if (!Assignments.Any(assignment => assignment.Contains(instant))) return null;
        var revision = Revisions.FirstOrDefault(candidate => candidate.Contains(instant));
        return revision is null
            ? null
            : new CourseLayoutGeometry(LayoutId, revision.Segments
                .Select(segment => new CourseSegmentGeometry(segment.Order, segment.StartPointId, segment.EndPointId, segment.Length))
                .ToList());
    }
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
/// resolves every instant locally: associations, layout assignments and layout revisions are all time-bound.
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

    /// <summary>
    /// Distance between two course points over the layouts in use at <paramref name="instant"/>, each with its
    /// geometry then, by the rules shared with Facilities (<see cref="CourseDistanceRules"/>).
    /// </summary>
    public CourseDistanceResult? ResolveDistance(Guid start, Guid end, DateTime instant) =>
        CourseDistanceRules.Resolve(Layouts.Select(layout => layout.GeometryAt(instant)).OfType<CourseLayoutGeometry>(), start, end);
}

/// <summary>An installation's facility over [ValidFromUtc, ValidToUtc); null is unbounded.</summary>
public sealed class InstallationFacilityVersion
{
    public Guid FacilityId { get; init; }
    public DateTime? ValidFromUtc { get; init; }
    public DateTime? ValidToUtc { get; init; }

    public bool Contains(DateTime instant) =>
        (ValidFromUtc is not { } from || from <= instant) && (ValidToUtc is not { } to || instant < to);
}

/// <summary>An installation's whole facility assignment history (plan §4.2, C2), resolved per instant.</summary>
public sealed class InstallationFacilityHistory
{
    public Guid InstallationId { get; init; }
    public List<InstallationFacilityVersion> Assignments { get; init; } = [];

    /// <summary>The facility the installation served at <paramref name="instant"/>, if exactly one.</summary>
    public Guid? ResolveFacility(DateTime instant)
    {
        Guid? found = null;
        foreach (var assignment in Assignments)
        {
            if (assignment.FacilityId == Guid.Empty || !assignment.Contains(instant)) continue;
            if (found.HasValue && found != assignment.FacilityId) return null;
            found = assignment.FacilityId;
        }
        return found;
    }
}
