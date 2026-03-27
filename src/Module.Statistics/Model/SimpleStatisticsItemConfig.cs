namespace VeloTime.Module.Statistics.Model;

public abstract class StatisticsItemConfig
{
    public Guid Id { get; set; }
    public required StatisticsItem StatisticsItem { get; set; }
    public Guid StatisticsItemId { get; set; }
}

public class SimpleStatisticsItemConfig : StatisticsItemConfig
{
    public Guid CoursePointStart { get; set; }
    public Guid CoursePointEnd { get; set; }
}

public class  MultiStatisticsItemConfig : StatisticsItemConfig
{
    public required SimpleStatisticsItemConfig ParentConfig { get; set; }
    public Guid ParentConfigId { get; set; }
    public required int Repetitions { get; set; }
}