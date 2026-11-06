namespace VeloTime.Agent.Model;

public class InstallationConfig : IModel
{
    public IEnumerable<SystemConfig> Systems { get; set; } = Array.Empty<SystemConfig>();
}
