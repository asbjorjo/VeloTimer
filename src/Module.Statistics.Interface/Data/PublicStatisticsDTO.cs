namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>Read-only discovery independent of whether results have been recorded.</summary>
public sealed record PublicStatisticsDiscoveryDTO(
    IReadOnlyList<StatisticsItemDTO> Items,
    IReadOnlyList<PublicStatisticsConfigurationDTO> Configurations,
    IReadOnlyList<UnavailablePublicStatisticsConfigurationDTO> UnavailableConfigurations);

/// <summary>
/// Both endpoints resolve to FacilityId. For Multi, the endpoint IDs/names are inherited
/// from its Simple parent; ParentConfigId and Repetitions describe the localized label.
/// </summary>
public sealed record PublicStatisticsConfigurationDTO(
    Guid Id,
    Guid StatisticsItemId,
    Guid FacilityId,
    StatisticsConfigurationType Type,
    Guid CoursePointStart,
    string CoursePointStartName,
    Guid CoursePointEnd,
    string CoursePointEndName,
    Guid? ParentConfigId,
    int? Repetitions);

public enum PublicStatisticsConfigurationUnavailableReason
{
    MissingItem,
    MissingSimpleParent,
    MissingCoursePoint,
    MissingFacility,
    DifferentFacilities,
    InvalidDefinition
}

/// <summary>Legacy inconsistencies are unavailable, not selectable. Upstream outages are errors instead.</summary>
public sealed record UnavailablePublicStatisticsConfigurationDTO(
    StatisticsConfigurationDTO Configuration,
    PublicStatisticsConfigurationUnavailableReason Reason);

/// <summary>One recorded entry. SpeedKmh is km/h; Duration retains storage precision.</summary>
public sealed record PublicStatisticsResultDTO(
    Guid Id,
    string ProfileName,
    Guid StatisticsItemId,
    Guid StatisticsItemConfigId,
    Guid FacilityId,
    TimeSpan Duration,
    double SpeedKmh,
    DateTimeOffset TimeEnd);

/// <summary>UTC PostgreSQL completion timestamp and native UUID key; never an anchor-row lookup.</summary>
public sealed record PublicStatisticsCursorDTO(DateTimeOffset TimeEnd, Guid Id);

public enum PublicStatisticsPageDirection
{
    Next,
    Previous
}

/// <summary>
/// Optional inclusive bounds. Name is normalized with Trim(), then matched as a literal
/// case-insensitive substring. Cursor and Direction must either both be absent or both present.
/// </summary>
public sealed record GetPublicStatisticsResultsRequest(
    Guid FacilityId,
    Guid StatisticsItemId,
    Guid StatisticsItemConfigId,
    string? Name = null,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null,
    double? MinSpeedKmh = null,
    double? MaxSpeedKmh = null,
    PublicStatisticsCursorDTO? Cursor = null,
    PublicStatisticsPageDirection? Direction = null);

/// <summary>
/// At most 25 newest-first rows, no count. Previous/Next are exclusive boundary cursors
/// to send with their corresponding direction; null means no page in that direction.
/// </summary>
public sealed record PublicStatisticsResultsPageDTO(
    IReadOnlyList<PublicStatisticsResultDTO> Items,
    PublicStatisticsCursorDTO? Previous,
    PublicStatisticsCursorDTO? Next);
