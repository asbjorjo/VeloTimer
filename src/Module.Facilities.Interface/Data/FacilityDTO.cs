namespace VeloTime.Module.Facilities.Interface.Data;

public class FacilityDTO
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public ICollection<Guid> Layouts { get; set; } = new List<Guid>();
}
