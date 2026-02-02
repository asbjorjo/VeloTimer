namespace VeloTime.Module.Statistics.Model;

public class StatisticsEntry
{
    public Guid Id { get; init; }
    public DateTime TimeStart { get; init; }
    public DateTime TimeEnd { get; init; }
    public required StatisticsItem StatisticsItem { get; init; }
    public Guid TransponderId { get; init; }
    public StatsProfile? StatsProfile { get; init; }
    public TimeSpan Duration { get; init; }
    public double Speed { get; init; }
    public Guid StatisticsItemId { get; init; }
}
