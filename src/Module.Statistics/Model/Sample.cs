namespace VeloTime.Module.Statistics.Model;

/// <summary>
/// Two consecutive resolved passings of one transponder in one session. Derived by the recompute worker;
/// <see cref="Id"/> is deterministic over (<see cref="StartPassingId"/>, <see cref="EndPassingId"/>).
/// </summary>
public class Sample
{
    public Guid Id { get; init; }
    public Guid StartPassingId { get; init; }
    public Guid EndPassingId { get; init; }
    public DateTime TimeStart { get; set; }
    public DateTime TimeEnd { get; set; }
    public Guid TransponderId { get; init; }
    public Guid CoursePointStartId { get; set; }
    public Guid CoursePointEndId { get; set; }
    public TimeSpan Duration { get; set; }
    public double Distance { get; set; }
    /// <summary>Layout identity from Facilities closed-loop evidence, when available.</summary>
    public Guid? CourseLayoutId { get; set; }
    /// <summary>Full closed-loop length from Facilities in metres, not endpoint distance.</summary>
    public double? ClosedLoopDistanceMetres { get; set; }
    public double Speed { get; set; }
    public Guid FacilityId { get; set; }
}
