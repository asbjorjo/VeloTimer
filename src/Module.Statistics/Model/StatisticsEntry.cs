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
    public StatsProfile? StatsProfile { get; init; }
    public TimeSpan Duration { get; init; }
    public double Speed { get; init; }
    public Guid FacilityId { get; init; }
}
