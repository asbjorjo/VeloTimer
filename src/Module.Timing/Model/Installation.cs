namespace VeloTime.Module.Timing.Model;

public class Installation {
    public Guid Id { get; init; }
    public Guid Facility { get; set; } = Guid.Empty;
    public required string AgentId { get; set; }
    public TimingSystem TimingSystem { get; set; } = TimingSystem.Unknown;
    public string Description { get; set; } = string.Empty;
    public List<TimingPoint> TimingPoints { get; init; } = new List<TimingPoint>();
}
