namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>
/// Selects the authoritative source of an item distance. Numeric values are stable on the wire.
/// </summary>
public enum StatisticsDistanceMode
{
    Fixed = 0,
    CourseLayout = 1
}

/// <summary>Statistics item definition. Distance is metres and is authoritative only in Fixed mode.
/// An omitted or null DistanceMode in a legacy response means Fixed. In CourseLayout mode Distance
/// is zero solely for wire compatibility and MUST NOT be used as an actual or fallback distance.
/// </summary>
public sealed record StatisticsItemDTO(
    Guid Id,
    string Name,
    string Description,
    float Distance,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null)
{
    /// <summary>Current responses set the mode; omitted/null in a legacy response means Fixed.</summary>
    public StatisticsDistanceMode? DistanceMode { get; init; }
}

/// <summary>Save a Statistics item. On create, omitted/null DistanceMode means Fixed. On update,
/// omitted/null preserves the existing mode so legacy clients cannot accidentally change it.
/// Specify a mode to change it. CourseLayout uses Distance=0 only as a non-authoritative sentinel.
/// </summary>
public sealed record SaveStatisticsItemRequest(
    string Name,
    string Description,
    float Distance,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null)
{
    /// <summary>POST omission/null means Fixed; PUT omission/null preserves the stored mode. An explicit value changes it.</summary>
    public StatisticsDistanceMode? DistanceMode { get; init; }

    /// <summary>
    /// PUT only: UTC instant from which changed distance, mode and bounds apply to results. Omitted means now.
    /// Results before it keep the values that held then; backdating recomputes results from that instant.
    /// </summary>
    public DateTime? EffectiveFromUtc { get; init; }
}

public enum StatisticsConfigurationType
{
    Simple,
    Multi
}

public sealed record StatisticsConfigurationDTO(
    Guid Id,
    Guid StatisticsItemId,
    StatisticsConfigurationType Type,
    Guid? CoursePointStart,
    Guid? CoursePointEnd,
    Guid? ParentConfigId,
    int? Repetitions,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null)
{
    /// <summary>
    /// Simple requirements, or null when none are configured. Multi always returns null and
    /// inherits its Simple parent. An absent property in an older response also means none known.
    /// </summary>
    public StatisticsRequiredCoursePoints? RequiredCoursePoints { get; init; }
}


public sealed record SaveStatisticsConfigurationRequest(
    Guid StatisticsItemId,
    StatisticsConfigurationType Type,
    Guid? CoursePointStart,
    Guid? CoursePointEnd,
    Guid? ParentConfigId,
    int? Repetitions,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null)
{
    /// <summary>
    /// For Simple create, omitted or null means no requirements. For Simple update, omitted or
    /// null preserves existing requirements (including the layout); a non-null value replaces them.
    /// Validate the resulting requirements against the proposed endpoints even when preserved.
    /// To clear, supply an object with null/omitted CourseLayoutId and an empty CoursePointIds array.
    /// A null/omitted CoursePointIds inside the object is invalid, not a clear. Multi requests must
    /// omit this property or set it to null; Multi inherits its parent.
    /// </summary>
    public StatisticsRequiredCoursePoints? RequiredCoursePoints { get; init; }

    /// <summary>POST only: UTC instant from which the new configuration counts results. Omitted means all recorded history.</summary>
    public DateTime? ValidFromUtc { get; init; }

    /// <summary>
    /// PUT only: UTC instant from which changed values apply to results. Omitted means now. Results before it keep
    /// the values that held then; backdating recomputes results from that instant.
    /// </summary>
    public DateTime? EffectiveFromUtc { get; init; }
}

/// <summary>
/// One dated version of a configuration over [ValidFromUtc, ValidToUtc) (null bounds are unbounded): every value
/// its results were derived with, including the item's distance, mode and bounds at the time.
/// </summary>
public sealed record StatisticsConfigurationVersionDTO(
    Guid Id,
    Guid StatisticsItemConfigId,
    Guid StatisticsItemId,
    DateTime? ValidFromUtc,
    DateTime? ValidToUtc,
    float Distance,
    StatisticsDistanceMode DistanceMode,
    TimeSpan? ItemMinDuration,
    TimeSpan? ItemMaxDuration,
    TimeSpan? MinDuration,
    TimeSpan? MaxDuration,
    Guid? CoursePointStart,
    Guid? CoursePointEnd,
    StatisticsRequiredCoursePoints? RequiredCoursePoints,
    Guid? ParentConfigId,
    int? Repetitions);