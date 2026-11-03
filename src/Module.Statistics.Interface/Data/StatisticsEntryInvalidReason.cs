namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>
/// The first applicable reason an entry is invalid. Evaluators check nonpositive
/// duration first, invalid contributors next, then the entry's effective duration bounds.
/// </summary>
public enum StatisticsEntryInvalidReason
{
    NonPositiveDuration,
    InvalidContributor,
    BelowMinimumDuration,
    AboveMaximumDuration
}
