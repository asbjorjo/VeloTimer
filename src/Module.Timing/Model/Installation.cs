namespace VeloTime.Module.Timing.Model;

public class Installation {
    public required Guid Id { get; init; }
    public required Guid Facility { get; init; }
    public required TimingSystem TimingSystem { get; init; }
    public string Description { get; set; } = string.Empty;
    public List<TimingPoint> TimingPoints { get; init; } = new List<TimingPoint>();
}
