namespace VeloTime.Module.Timing.Model;

public class TimingPoint
{
    public required Guid Id { get; init; }
    public required string SystemId { get; set; }
    public Installation Installation { get; init; } = null!;
    public required string Description { get; set; }
}
