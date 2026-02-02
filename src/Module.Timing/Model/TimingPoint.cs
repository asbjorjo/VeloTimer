namespace VeloTime.Module.Timing.Model;

public class TimingPoint
{
    public Guid Id { get; init; }
    public required string SystemId { get; set; }
    public Installation Installation { get; init; } = null!;
    public Guid InstallationId { get; init; }
    public required string Description { get; set; }
}
