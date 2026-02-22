namespace VeloTime.Module.Facilities.Interface.Data;

public class CoursePointDTO
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public Guid? TimingPointId { get; set; }
}