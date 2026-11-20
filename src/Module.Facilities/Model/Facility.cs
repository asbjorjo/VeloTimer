namespace VeloTime.Module.Facilities.Model;

public class Facility
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public ICollection<CourseLayout> Layouts { get; set; } = new List<CourseLayout>();
}
