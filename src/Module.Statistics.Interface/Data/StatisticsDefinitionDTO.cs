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
/// A null DistanceMode is the legacy Fixed mode. In CourseLayout mode Distance is zero solely for
/// wire compatibility and MUST NOT be used as an actual or fallback distance.
/// </summary>
public sealed record StatisticsItemDTO(
    Guid Id,
    string Name,
    string Description,
    float Distance,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null)
{
    /// <summary>Omitted/null means Fixed for responses produced before this discriminator existed.</summary>
    public StatisticsDistanceMode? DistanceMode { get; init; }
}

/// <summary>Save a Statistics item. Omitted/null DistanceMode means Fixed for legacy callers.
/// CourseLayout requests send Distance=0 only as a non-authoritative compatibility sentinel.
/// </summary>
public sealed record SaveStatisticsItemRequest(
    string Name,
    string Description,
    float Distance,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null)
{
    /// <summary>Omitted/null means Fixed; CourseLayout is explicit and never inferred from the name.</summary>
    public StatisticsDistanceMode? DistanceMode { get; init; }
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
}