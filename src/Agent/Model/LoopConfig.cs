namespace VeloTime.Agent.Model;

public class LoopConfig : IModel
{
    public required string LoopId { get; init; }
    public required string Name { get; init; }
    public required int Order { get; init; }
    public required string SystemId { get; init; }
}
