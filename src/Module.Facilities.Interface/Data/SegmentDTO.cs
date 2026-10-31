namespace VeloTime.Module.Facilities.Interface.Data;

public class SegmentDTO
{
    public required Guid Id { get; set; }
    public int Order { get; set; }
    public required CoursePointDTO Start { get; set; }
    public required CoursePointDTO End { get; set; }
    public double Length { get; set; }
}
