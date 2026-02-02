namespace VeloTime.Module.Statistics.Model;

public class SimpleStatisticsItem
{
    public Guid Id { get; set; }
    public required StatisticsItem StatisticsItem { get; set; }
    public Guid CoursePointStart { get; set; }
    public Guid CoursePointEnd { get; set; }
    public Guid StatisticsItemId { get; set; }
}
