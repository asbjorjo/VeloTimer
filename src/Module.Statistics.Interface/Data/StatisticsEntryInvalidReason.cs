namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>
/// The displayed reason an entry is invalid. A manual invalidation takes precedence while
/// it is active; otherwise evaluators check nonpositive duration, invalid contributors,
/// unsatisfied required course points, then the entry's effective duration bounds.
/// Checkpoint satisfaction is retained separately from this displayed reason so revalidation
/// cannot erase concurrent failures.
/// </summary>
public enum StatisticsEntryInvalidReason
{
    NonPositiveDuration = 0,
    InvalidContributor = 1,
    BelowMinimumDuration = 2,
    AboveMaximumDuration = 3,
    RequiredCoursePointsNotSatisfied = 4,
    ManuallyInvalidated = 5
}
