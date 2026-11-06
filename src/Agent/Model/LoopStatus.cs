namespace VeloTime.Agent.Model;

public class LoopStatus : IModel
{
    public required DateTime Time { get; init; }
    public required string LoopId { get; init; }
    public double Noise { get; init; }
}
