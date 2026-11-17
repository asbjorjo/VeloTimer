using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Module.Statistics.Model;

public class StatisticsEntry
{
    public Guid Id { get; init; }
    public DateTime TimeStart { get; init; }
    public DateTime TimeEnd { get; init; }
    public StatisticsItem StatisticsItem { get; init; } = default!;
    public Guid StatisticsItemId { get; init; }
    public StatisticsItemConfig StatisticsItemConfig { get; init; } = default!;
    public Guid StatisticsItemConfigId { get; init; }
    public Guid TransponderId { get; init; }
    public StatsProfile? StatsProfile { get; set; }
    public Guid? StatsProfileUserId { get; set; }
    public TimeSpan Duration { get; init; }
    public double Speed { get; init; }
    public Guid FacilityId { get; init; }
    /// <summary>Attempt-time checkpoint outcome, independent of InvalidReason. False is retained
    /// even when another failure has display precedence. True for legacy/unconfigured entries
    /// and Multi entries (which use contributor validity). Never reassess after late arrivals
    /// or threshold edits.</summary>
    public bool RequiredCoursePointsSatisfied { get; init; } = true;
    public bool IsValid { get; set; } = true;
    public StatisticsEntryInvalidReason? InvalidReason { get; set; }
    public Guid? ManualInvalidationChangedByUserId { get; set; }
    public DateTimeOffset? ManualInvalidationChangedAtUtc { get; set; }
    public string? ManualInvalidationNote { get; set; }
}
