namespace VeloTime.Module.Facilities.Model;

public class CourseLayout
{
    public Guid Id { get; init; }
    public Guid FacilityId { get; init; }
    public Facility Facility { get; init; } = default!;
    public IList<CourseSegment> Segments { get; set; } = new List<CourseSegment>();
}

public class CourseSegment
{
    public Guid Id { get; init; }
    public CourseLayout CourseLayout { get; init; } = default!;
    public Guid CourseLayoutId { get; init; }
    public int Order { get; set; }
    public required CoursePoint Start { get; set; }
    public required CoursePoint End { get; set; }
    public double Length { get; set; }
}

public class CoursePoint
{
    public Guid Id { get; init; }
    public String? Name { get; set; }
    public Guid? TimingPoint {  get; set; }
}
