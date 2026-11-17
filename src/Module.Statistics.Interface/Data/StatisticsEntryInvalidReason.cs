namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>
/// The first applicable reason an entry is invalid. Evaluators check nonpositive
/// duration first, invalid contributors next, then unsatisfied required course points,
/// then the entry's effective duration bounds. Checkpoint satisfaction is retained separately
/// from this displayed reason so revalidation cannot erase concurrent failures.
/// </summary>
public enum StatisticsEntryInvalidReason
{
    NonPositiveDuration = 0,
    InvalidContributor = 1,
    BelowMinimumDuration = 2,
    AboveMaximumDuration = 3,
    RequiredCoursePointsNotSatisfied = 4
}
