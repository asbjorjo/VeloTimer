namespace VeloTime.Module.Timing.Interface.Data;

public class InstallationDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid Facility { get; init; }
    public List<TimingPointDto> TimingPoints { get; init; } = new();
    public required TimingSystemDto TimingSystem { get; set; }
    public string Description { get; set; } = string.Empty;
}
