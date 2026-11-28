namespace VeloTime.Module.Facilities.Interface.Data;

/// <summary>
/// Replaces a layout's geometry over the half-open UTC interval [ValidFromUtc, ValidToUtc) with a new revision.
/// Null bounds are unbounded; both null replaces the whole history. Existing revisions outside the interval are kept.
/// Backdating is allowed: the change event makes Statistics recompute results from ValidFromUtc.
/// </summary>
public sealed record SetCourseLayoutRevisionRequest
{
    public required IReadOnlyList<SaveCoursePointRequest> Points { get; init; }
    /// <summary>Segments in course order. Segment IDs are assigned by the server; any supplied ID is ignored.</summary>
    public required IReadOnlyList<SaveCourseSegmentRequest> Segments { get; init; }
    public required DateTimeOffset? ValidFromUtc { get; init; }
    public required DateTimeOffset? ValidToUtc { get; init; }
    /// <summary>
    /// Omit on the initial request. When the interval overlaps existing revisions the server answers 409 with a
    /// <see cref="CourseLayoutRevisionConflictPreview"/>; echo its token to confirm the replacement.
    /// </summary>
    public string? ConfirmationToken { get; init; }
}

/// <summary>An existing revision the requested interval replaces, wholly or in part.</summary>
public sealed record CourseLayoutRevisionOverlap(
    Guid RevisionId,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    DateTimeOffset? OverlapValidFromUtc,
    DateTimeOffset? OverlapValidToUtc,
    double TotalLength,
    int SegmentCount);

/// <summary>
/// Structured 409 preview. Code is "revision-overlap" for an initial preview and "confirmation-stale" when the
/// supplied confirmation no longer matches the layout's current revisions.
/// </summary>
public sealed record CourseLayoutRevisionConflictPreview(
    string Code,
    Guid CourseLayoutId,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    IReadOnlyList<CourseLayoutRevisionOverlap> Overlaps,
    string ConfirmationToken);

/// <summary>Result of a revision edit. A no-op (same geometry throughout) has no ChangeId or RevisionId.</summary>
public sealed record SetCourseLayoutRevisionResponse(Guid? ChangeId, Guid? RevisionId);

/// <summary>Sets whether the facility runs a layout over [ValidFromUtc, ValidToUtc). Null bounds are unbounded.</summary>
public sealed record SetFacilityLayoutAssignmentRequest
{
    public required bool InUse { get; init; }
    public required DateTimeOffset? ValidFromUtc { get; init; }
    public required DateTimeOffset? ValidToUtc { get; init; }
}

/// <summary>Result of an assignment edit. A no-op has no ChangeId.</summary>
public sealed record SetFacilityLayoutAssignmentResponse(Guid? ChangeId, IReadOnlyList<EffectiveIntervalDTO> Assignments);
