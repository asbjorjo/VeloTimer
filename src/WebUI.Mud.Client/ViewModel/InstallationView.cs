namespace VeloTime.WebUI.Mud.Client.ViewModel;

public class InstallationView
{
    public Guid Id { get; init; }
    public string AgentId { get; set; } = string.Empty;
    public string TimingSystem { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IEnumerable<TimingPointView> TimingPoints { get; set; } = Array.Empty<TimingPointView>();
    public bool IsSelected { get; set; }

}

public class TimingPointView
{
    public Guid Id { get; init; }
    public required string Description { get; set; }
    public required string SystemId { get; set; }
}
