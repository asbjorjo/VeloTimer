namespace VeloTime.Module.Facilities.Interface.Data;

/// <summary>
/// Sets a coursepoint's timing-point association over an explicit half-open UTC interval.
/// Both bound properties are required in JSON; a null value means that side is unbounded.
/// </summary>
public sealed record SetCoursePointTimingAssociationRequest
{
    /// <summary>The timing point to associate, or null to unlink over the requested interval.</summary>
    public required Guid? TimingPointId { get; init; }

    /// <summary>Inclusive lower bound, or null for negative infinity.</summary>
    public required DateTimeOffset? ValidFromUtc { get; init; }

    /// <summary>Exclusive upper bound, or null for positive infinity.</summary>
    public required DateTimeOffset? ValidToUtc { get; init; }

    /// <summary>
    /// Omit on the initial request. On confirmation, echo the exact token returned by the
    /// conflict preview; the server validates it against this request and current conflict state.
    /// </summary>
    public string? ConfirmationToken { get; init; }
}

/// <summary>
/// A coursepoint-to-timingpoint mapping interval. Null bounds are unbounded.
/// </summary>
public sealed record CoursePointTimingAssociationSnapshot(
    Guid TimingPointId,
    Guid CoursePointId,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc);

/// <summary>
/// One effective mapping delta. Null Before/After means no mapping on that side.
/// Present snapshots describe only the exact affected interval.
/// </summary>
public sealed record CoursePointTimingAssociationChange(
    CoursePointTimingAssociationSnapshot? Before,
    CoursePointTimingAssociationSnapshot? After);

/// <summary>
/// Result of an explicit interval edit. A no-op has no ChangeId and an empty Changes list.
/// </summary>
public sealed record SetCoursePointTimingAssociationResponse(
    Guid? ChangeId,
    IReadOnlyList<CoursePointTimingAssociationChange> Changes);

/// <summary>The proposed mapping interval shown in a destination-conflict preview.</summary>
public sealed record TimingAssociationRequestedChange(
    Guid CoursePointId,
    Guid? TimingPointId,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc);

/// <summary>
/// An existing association occupying part of the requested interval. Association bounds are
/// the complete existing interval; Overlap bounds identify the exact conflicting portion.
/// </summary>
public sealed record TimingAssociationDestinationConflict(
    CoursePointTimingAssociationSnapshot Association,
    DateTimeOffset? OverlapValidFromUtc,
    DateTimeOffset? OverlapValidToUtc);

/// <summary>
/// Structured 409 preview. Code is "destination-occupied" for an initial preview and
/// "confirmation-stale" when the supplied confirmation no longer matches current state.
/// </summary>
public sealed record TimingAssociationConflictPreview(
    string Code,
    TimingAssociationRequestedChange Requested,
    IReadOnlyList<TimingAssociationDestinationConflict> Conflicts,
    string ConfirmationToken);
