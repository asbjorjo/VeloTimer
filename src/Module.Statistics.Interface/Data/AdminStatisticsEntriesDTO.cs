namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>
/// An administrative view of one recorded entry. Times are UTC, duration is the stored
/// duration, and speed is in metres per second. Speed is null when the stored value is
/// not finite. The manual audit fields describe the latest manual invalidation state change
/// and may be absent for entries that have never been changed manually.
/// </summary>
public sealed record AdminStatisticsEntryDTO(
    Guid Id,
    DateTimeOffset TimeStart,
    DateTimeOffset TimeEnd,
    Guid StatisticsItemId,
    string StatisticsItemName,
    Guid StatisticsItemConfigId,
    StatisticsConfigurationType StatisticsItemConfigType,
    Guid FacilityId,
    Guid TransponderId,
    Guid? StatsProfileUserId,
    string? ProfileName,
    TimeSpan Duration,
    double? SpeedMetresPerSecond,
    bool IsValid,
    StatisticsEntryInvalidReason? InvalidReason,
    bool IsManuallyInvalidated,
    Guid? ManualInvalidationChangedByUserId,
    DateTimeOffset? ManualInvalidationChangedAtUtc,
    string? ManualInvalidationNote);

/// <summary>
/// Stable exclusive keyset boundary, tagged with its sort and order. TimeEnd is UTC and
/// used only for TimeEnd sorting (default otherwise); Duration or Speed is populated only
/// for its corresponding sort. Speed is an invariant round-trip string, including NaN,
/// Infinity and -Infinity, so ordinary JSON serialization never writes non-finite numbers.
/// PostgreSQL native speed ordering is -Infinity, finite values, Infinity, NaN.
/// </summary>
public sealed record AdminStatisticsEntryCursorDTO(
    DateTimeOffset TimeEnd,
    Guid Id,
    AdminStatisticsEntriesSort Sort = AdminStatisticsEntriesSort.TimeEnd,
    AdminStatisticsEntriesSortOrder Order = AdminStatisticsEntriesSortOrder.Descending,
    TimeSpan? Duration = null,
    string? Speed = null);

/// <summary>Primary key for administrative entry ordering; UUID is the stable tie-break.</summary>
public enum AdminStatisticsEntriesSort { TimeEnd, Duration, Speed }

/// <summary>Applies to both the primary key and UUID tie-break.</summary>
public enum AdminStatisticsEntriesSortOrder { Descending, Ascending }

public enum AdminStatisticsEntriesPageDirection
{
    Next,
    Previous
}

/// <summary>
/// Keyset-paged administrative listing, defaulting to newest first by TimeEnd and then Id.
/// Duration bounds are inclusive and signed (zero is allowed). Speed bounds are inclusive,
/// finite, nonnegative metres per second; any speed bound excludes non-finite stored speeds.
/// Sorting without speed bounds includes non-finite speeds in PostgreSQL native order.
/// Cursor and Direction must be supplied together. PageSize is limited by the API to 1–100.
/// </summary>
public sealed record GetAdminStatisticsEntriesRequest(
    Guid? FacilityId = null,
    Guid? StatisticsItemId = null,
    Guid? StatisticsItemConfigId = null,
    Guid? TransponderId = null,
    bool? IsValid = null,
    AdminStatisticsEntryCursorDTO? Cursor = null,
    AdminStatisticsEntriesPageDirection? Direction = null,
    int PageSize = 50,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null,
    double? MinSpeedMetresPerSecond = null,
    double? MaxSpeedMetresPerSecond = null,
    AdminStatisticsEntriesSort Sort = AdminStatisticsEntriesSort.TimeEnd,
    AdminStatisticsEntriesSortOrder Order = AdminStatisticsEntriesSortOrder.Descending);

/// <summary>
/// A keyset page without a total count. Previous and Next are exclusive boundary cursors
/// for their corresponding directions; null means that direction has no further page.
/// </summary>
public sealed record AdminStatisticsEntriesPageDTO(
    IReadOnlyList<AdminStatisticsEntryDTO> Items,
    AdminStatisticsEntryCursorDTO? Previous,
    AdminStatisticsEntryCursorDTO? Next);

/// <summary>
/// Sets or restores the manual invalidation state. False explicitly restores automatic
/// validity evaluation; manual invalidation remains sticky until this request restores it.
/// Note is optional and is limited to 500 characters by the API. It describes the requested
/// state change, whether marking or restoring.
/// </summary>
public sealed record StatisticsEntryManualInvalidationRequest(
    bool IsManuallyInvalidated,
    string? Note = null)
{
    public const int MaximumNoteLength = 500;
}
