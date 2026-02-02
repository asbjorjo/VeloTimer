namespace VeloTime.Agent.Model;

public class SystemConfig : IModel
{
    public required string SystemId { get; init; }
    public required string Sport { get; init; }
    public required string Description { get; init; }
    public bool IsActive { get; init; } = true;

    public IEnumerable<LoopConfig> Loops { get; set; } = Array.Empty<LoopConfig>();
    public IEnumerable<SegmentConfig> Segments { get; set; } = Array.Empty<SegmentConfig>();
}
