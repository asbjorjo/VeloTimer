namespace VeloTime.Module.Timing.Model;

public class Passing {
    public Guid Id { get; init; }
    public Guid TransponderId { get; init; }
    public Guid TimingPointId { get; init; }
    public required DateTime Time { get; init; }
    public Transponder Transponder { get; init; } = null!;
    public TimingPoint TimingPoint { get; init; } = null!;
    public bool LowBattery { get; init; } = false;
    public bool LowStrength { get; init; } = false;
    public bool LowHits { get; init; } = false;
}
