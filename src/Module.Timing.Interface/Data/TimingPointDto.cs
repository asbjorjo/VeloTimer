namespace VeloTime.Module.Timing.Interface.Data;

public class TimingPointDTO
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Description { get; set; }
    public required string SystemId { get; set; }
}
