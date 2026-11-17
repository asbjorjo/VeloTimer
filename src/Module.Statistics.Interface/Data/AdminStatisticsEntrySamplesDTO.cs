namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>
/// Samples fully contained in the authoritative entry window, inclusive at both ends,
/// for its transponder. Times have UTC offsets. Samples are ordered by TimeStart,
/// TimeEnd, then Id. At most 500 samples are returned; Truncated means more matched.
/// This is a window-based drill-down, not a persisted provenance relationship.
/// </summary>
public sealed record AdminStatisticsEntrySamplesDTO(
    Guid EntryId,
    Guid TransponderId,
    DateTimeOffset TimeStart,
    DateTimeOffset TimeEnd,
    IReadOnlyList<AdminStatisticsSampleDTO> Samples,
    bool Truncated);

/// <summary>
/// One recorded Statistics sample. Times have UTC offsets; distance is finite metres,
/// duration is the stored duration, and speed is metres per second (null when nonfinite).
/// A nonfinite stored distance causes an explicit 409 rather than an invented measurement.
/// </summary>
public sealed record AdminStatisticsSampleDTO(
    Guid Id,
    DateTimeOffset TimeStart,
    DateTimeOffset TimeEnd,
    Guid CoursePointStartId,
    Guid CoursePointEndId,
    double DistanceMetres,
    TimeSpan Duration,
    double? SpeedMetresPerSecond,
    Guid FacilityId);
