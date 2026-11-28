namespace VeloTime.Module.Timing.Interface.Data;

public class InstallationDTO
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FacilityId { get; set; } = Guid.Empty;
    public string AgentId { get; set; } = string.Empty;
    public DateTime? LastSeen { get; set; }
    public List<TimingPointDTO> TimingPoints { get; init; } = new();
    public required TimingSystemDTO TimingSystem { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>The agent's latest capability report; null when the agent has never sent one.</summary>
    public AgentCapabilitiesDTO? Capabilities { get; set; }
}
