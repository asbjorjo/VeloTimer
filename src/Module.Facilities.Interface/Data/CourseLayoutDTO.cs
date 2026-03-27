namespace VeloTime.Module.Facilities.Interface.Data;

public class CourseLayoutDTO
{
    public required Guid Id { get; init; }
    public required Guid FacilityId { get; init; }
    public ICollection<SegmentDTO> Segments { get; set; } = new List<SegmentDTO>();
}
