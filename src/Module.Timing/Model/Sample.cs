namespace VeloTime.Module.Timing.Model;

public class Sample
{
    public Guid Id { get; init; }
    public Passing Start { get; init; } = null!;
    public Passing End { get; init; } = null!;

    public Guid StartId { get; init; }
    public Guid EndId { get; init; }
}
