namespace VeloTime.Module.Timing.Model;

public class TimingPoint
{
    public required Guid Id { get; init; }
    public required string SystemId { get; init; }
    public required Installation Installation { get; init; }
    public required string Description { get; init; }
}
