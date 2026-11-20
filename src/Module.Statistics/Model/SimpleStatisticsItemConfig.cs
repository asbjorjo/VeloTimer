namespace VeloTime.Module.Statistics.Model;

public abstract class StatisticsItemConfig
{
    public Guid Id { get; set; }
    public required StatisticsItem StatisticsItem { get; set; }
    public Guid StatisticsItemId { get; set; }
    public TimeSpan? MinDuration { get; set; }
    public TimeSpan? MaxDuration { get; set; }
}

public class SimpleStatisticsItemConfig : StatisticsItemConfig
{
    public Guid CoursePointStart { get; set; }
    public Guid CoursePointEnd { get; set; }

    /// <summary>The Facilities layout validated when the ordered requirements were saved;
    /// null when no points are required. This is an external ID, not a database relationship.</summary>
    public Guid? RequiredCourseLayoutId { get; set; }

    /// <summary>Immutable configured snapshot of intermediate point IDs in forward course order.
    /// Empty preserves legacy behavior. Replace with a copied array only at a validated structural
    /// save; never reorder from a subsequently changed Facilities layout or mutate in place.</summary>
    public Guid[] RequiredCoursePointIds { get; set; } = [];
}

public class  MultiStatisticsItemConfig : StatisticsItemConfig
{
    public required SimpleStatisticsItemConfig ParentConfig { get; set; }
    public Guid ParentConfigId { get; set; }
    public required int Repetitions { get; set; }
}