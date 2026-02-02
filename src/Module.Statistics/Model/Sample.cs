namespace VeloTime.Module.Statistics.Model;

public class Sample
{
    public Guid Id { get; init; }
    public DateTime TimeStart { get; init; }
    public DateTime TimeEnd { get; set; }
    public Guid TransponderId { get; init; }
    public Guid CoursePointStartId { get; init; }
    public Guid CoursePointEndId { get; init; }
    public TimeSpan Duration { get; init; }
    public double Distance { get; init; }
    public double Speed { get; init; }
}
