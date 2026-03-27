namespace VeloTime.Module.Timing.Interface.Data;

public class InstallationDTO
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string AgentId { get; set; } = string.Empty;
    public List<TimingPointDTO> TimingPoints { get; init; } = new();
    public required TimingSystemDTO TimingSystem { get; set; }
    public string Description { get; set; } = string.Empty;
}
