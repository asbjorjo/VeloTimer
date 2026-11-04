using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Service;

/// <summary>
/// Effective inclusive duration bounds. Null means unbounded on that side.
/// </summary>
public readonly record struct StatisticsDurationBounds(TimeSpan? Minimum, TimeSpan? Maximum)
{
    public static StatisticsDurationBounds Unbounded => new(null, null);

    /// <summary>
    /// Global bounds are hard limits: the effective minimum is the larger non-null minimum and the
    /// effective maximum the smaller non-null maximum. Local bounds can only narrow the global range.
    /// The result is not normalized; a disjoint range (minimum above maximum) rejects every duration.
    /// </summary>
    public static StatisticsDurationBounds Effective(TimeSpan? globalMinimum, TimeSpan? globalMaximum,
        TimeSpan? localMinimum, TimeSpan? localMaximum) =>
        new(Larger(globalMinimum, localMinimum), Smaller(globalMaximum, localMaximum));

    public static StatisticsDurationBounds Effective(StatisticsItem item, StatisticsItemConfig config) =>
        Effective(item.MinDuration, item.MaxDuration, config.MinDuration, config.MaxDuration);

    public bool IsDisjoint => Minimum.HasValue && Maximum.HasValue && Minimum.Value > Maximum.Value;

    private static TimeSpan? Larger(TimeSpan? first, TimeSpan? second) =>
        first.HasValue && second.HasValue ? (first.Value >= second.Value ? first : second) : first ?? second;

    private static TimeSpan? Smaller(TimeSpan? first, TimeSpan? second) =>
        first.HasValue && second.HasValue ? (first.Value <= second.Value ? first : second) : first ?? second;
}

/// <summary>
/// Shared classification for creation and historical revalidation. Measurements are never altered;
/// only <see cref="StatisticsEntry.IsValid"/> and <see cref="StatisticsEntry.InvalidReason"/> are derived.
/// </summary>
public static class StatisticsValidityEvaluator
{
    /// <summary>
    /// Returns the first applicable invalidity reason, or null when valid. Order: nonpositive duration
    /// (independent of bounds; also covers infinite/NaN raw speed), invalid contributor (Multi only),
    /// below the inclusive minimum, above the inclusive maximum.
    /// </summary>
    public static StatisticsEntryInvalidReason? Evaluate(TimeSpan duration, bool contributorsValid,
        StatisticsDurationBounds bounds)
    {
        if (duration <= TimeSpan.Zero) return StatisticsEntryInvalidReason.NonPositiveDuration;
        if (!contributorsValid) return StatisticsEntryInvalidReason.InvalidContributor;
        if (bounds.Minimum.HasValue && duration < bounds.Minimum.Value) return StatisticsEntryInvalidReason.BelowMinimumDuration;
        if (bounds.Maximum.HasValue && duration > bounds.Maximum.Value) return StatisticsEntryInvalidReason.AboveMaximumDuration;
        return null;
    }

    /// <summary>Applies a classification; returns true when stored validity changes.</summary>
    public static bool Apply(StatisticsEntry entry, StatisticsEntryInvalidReason? reason)
    {
        var valid = reason is null;
        if (entry.IsValid == valid && entry.InvalidReason == reason) return false;
        entry.IsValid = valid;
        entry.InvalidReason = reason;
        return true;
    }
}
