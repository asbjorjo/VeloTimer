using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Module.Statistics.Model;

/// <summary>
/// A derived result. Written only by the recompute worker, which identifies it deterministically
/// (Simple: configuration and its start and end passings; Multi: configuration and its first and last
/// contributors), so a recompute updates it in place instead of duplicating it.
/// </summary>
public class StatisticsEntry
{
    public Guid Id { get; init; }
    public DateTime TimeStart { get; set; }
    public DateTime TimeEnd { get; set; }
    public StatisticsItem StatisticsItem { get; init; } = default!;
    public Guid StatisticsItemId { get; init; }
    public StatisticsItemConfig StatisticsItemConfig { get; init; } = default!;
    public Guid StatisticsItemConfigId { get; init; }
    public Guid TransponderId { get; init; }
    public StatsProfile? StatsProfile { get; set; }
    public Guid? StatsProfileUserId { get; set; }
    public TimeSpan Duration { get; set; }
    public double Speed { get; set; }
    /// <summary>Distance in metres captured for this entry, when known.</summary>
    public double? DistanceMetres { get; set; }
    public Guid FacilityId { get; set; }
    /// <summary>Checkpoint outcome, independent of InvalidReason. False is retained even when another
    /// failure has display precedence. True for unconfigured entries and Multi entries (which use
    /// contributor validity). Each recompute re-evaluates it from the session's passings.</summary>
    public bool RequiredCoursePointsSatisfied { get; set; } = true;
    public bool IsValid { get; set; } = true;
    public StatisticsEntryInvalidReason? InvalidReason { get; set; }
    public Guid? ManualInvalidationChangedByUserId { get; set; }
    public DateTimeOffset? ManualInvalidationChangedAtUtc { get; set; }
    public string? ManualInvalidationNote { get; set; }
}
