namespace VeloTime.Module.Timing.Model;

public class Sample
{
    public Guid Id { get; init; }
    public required Passing Start { get; init; }
    public required Passing End { get; init; }

    public Guid StartId { get; init; }
    public Guid EndId { get; init; }
}
