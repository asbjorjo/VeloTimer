namespace VeloTime.Agent.Model;

public class Passing : IModel
{
    public required string Id { get; init; }
    public DateTime Time { get; init; }
    public required string TransponderType { get; init; }
    public required string TransponderId { get; init; }
    public required string LoopId { get; init; }
    public bool LowBattery { get; init; }
    public bool LowStrength { get; init; }
}
