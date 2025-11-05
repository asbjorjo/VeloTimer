namespace VeloTime.Module.Timing.Model;

public class TimingSystem {
    public required Guid Id { get; init; }
    public required string Name { get; set; }
    public List<Installation> Installations { get; init; } = new List<Installation>();
}
