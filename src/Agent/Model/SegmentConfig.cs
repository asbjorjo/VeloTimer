namespace VeloTime.Agent.Model;

public class SegmentConfig : IModel
{
    public required string SegmentId { get; init; }
    public required string Name { get; init; }
    public required string FromLoopId { get; init; }
    public required string ToLoopId { get; init; }
    public required double Length { get; init; } = 0;
    public required string SystemId { get; init; }
}
