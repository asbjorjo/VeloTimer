namespace VeloTime.Module.Facilities.Model;

/// <summary>
/// A course layout's identity. Its geometry lives in dated <see cref="Revisions"/>, and when the facility runs it
/// lives in dated <see cref="Assignments"/> (docs/replay-safe-statistics-plan.md §4.2). The ID stays the same across
/// revisions, so statistics definitions that require a layout keep referring to it.
/// </summary>
public class CourseLayout
{
    public Guid Id { get; init; }
    public Guid FacilityId { get; init; }
    public Facility Facility { get; init; } = default!;
    public IList<CourseLayoutRevision> Revisions { get; set; } = new List<CourseLayoutRevision>();
    public IList<FacilityLayoutAssignment> Assignments { get; set; } = new List<FacilityLayoutAssignment>();

    /// <summary>The revision in effect at <paramref name="at"/>, from the loaded revisions.</summary>
    public CourseLayoutRevision? RevisionAt(DateTimeOffset at) =>
        Revisions.FirstOrDefault(revision => revision.Contains(at));

    /// <summary>Whether the facility runs this layout at <paramref name="at"/>, from the loaded assignments.</summary>
    public bool InUseAt(DateTimeOffset at) => Assignments.Any(assignment => assignment.Contains(at));

    /// <summary>
    /// For seeding a new layout graph: the layout's unbounded revision, created with an unbounded assignment when the
    /// layout has none yet. Edits of stored layouts go through <c>CourseConfigurationService</c>.
    /// </summary>
    public CourseLayoutRevision EnsureUnboundedRevision()
    {
        if (Revisions.SingleOrDefault(revision => revision.ValidFromUtc is null && revision.ValidToUtc is null) is { } existing)
            return existing;
        if (Revisions.Count > 0) throw new InvalidOperationException("The layout already has dated revisions.");
        var facilityId = FacilityId != Guid.Empty ? FacilityId : Facility?.Id ?? Guid.Empty;
        if (Assignments.Count == 0)
            Assignments.Add(new FacilityLayoutAssignment { Id = Guid.NewGuid(), FacilityId = facilityId, CourseLayoutId = Id, CourseLayout = this });
        var revision = new CourseLayoutRevision { Id = Guid.NewGuid(), CourseLayoutId = Id, CourseLayout = this };
        Revisions.Add(revision);
        return revision;
    }
}

/// <summary>
/// The geometry (segments, order, lengths) of a layout over [ValidFromUtc, ValidToUtc). Null bounds are
/// unbounded, and revisions of one layout never overlap. A geometry edit creates a revision from an effective date;
/// it never changes a revision's segments in place.
/// </summary>
public class CourseLayoutRevision : IEffectiveInterval
{
    private DateTimeOffset? validFromUtc;
    private DateTimeOffset? validToUtc;

    public Guid Id { get; init; }
    public Guid CourseLayoutId { get; init; }
    public CourseLayout CourseLayout { get; init; } = default!;
    public IList<CourseSegment> Segments { get; set; } = new List<CourseSegment>();

    public DateTimeOffset? ValidFromUtc
    {
        get => validFromUtc;
        set => validFromUtc = value?.ToUniversalTime();
    }

    public DateTimeOffset? ValidToUtc
    {
        get => validToUtc;
        set => validToUtc = value?.ToUniversalTime();
    }

    public bool Contains(DateTimeOffset at) =>
        (!ValidFromUtc.HasValue || ValidFromUtc.Value <= at) && (!ValidToUtc.HasValue || at < ValidToUtc.Value);
}

/// <summary>
/// The facility runs <see cref="CourseLayoutId"/> over [ValidFromUtc, ValidToUtc). Null bounds are unbounded; the
/// assignments of one layout never overlap. Distance resolution at an instant considers only layouts in use then.
/// </summary>
public class FacilityLayoutAssignment : IEffectiveInterval
{
    private DateTimeOffset? validFromUtc;
    private DateTimeOffset? validToUtc;

    public Guid Id { get; init; }
    public Guid FacilityId { get; init; }
    public Guid CourseLayoutId { get; init; }
    public CourseLayout CourseLayout { get; init; } = default!;

    public DateTimeOffset? ValidFromUtc
    {
        get => validFromUtc;
        set => validFromUtc = value?.ToUniversalTime();
    }

    public DateTimeOffset? ValidToUtc
    {
        get => validToUtc;
        set => validToUtc = value?.ToUniversalTime();
    }

    public bool Contains(DateTimeOffset at) =>
        (!ValidFromUtc.HasValue || ValidFromUtc.Value <= at) && (!ValidToUtc.HasValue || at < ValidToUtc.Value);
}

public interface IEffectiveInterval
{
    DateTimeOffset? ValidFromUtc { get; set; }
    DateTimeOffset? ValidToUtc { get; set; }
}

public class CourseSegment
{
    public Guid Id { get; init; }
    public CourseLayoutRevision Revision { get; init; } = default!;
    public Guid CourseLayoutRevisionId { get; init; }
    public int Order { get; set; }
    public required CoursePoint Start { get; set; }
    public required CoursePoint End { get; set; }
    public double Length { get; set; }
}

public class CoursePoint
{
    public Guid Id { get; init; }
    public String? Name { get; set; }
    public ICollection<CoursePointTimingAssociation> TimingAssociations { get; set; } =
        new List<CoursePointTimingAssociation>();
}
