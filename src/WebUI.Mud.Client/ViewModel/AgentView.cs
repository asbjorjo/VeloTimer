namespace VeloTime.WebUI.Mud.Client.ViewModel;

public class AgentView
{
    public required string AgentId { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public InstallationView? Installation { get; set; }
}
