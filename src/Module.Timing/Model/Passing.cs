namespace VeloTime.Module.Timing.Model;

public class Passing {
    public required Guid Id { get; init; }
    public required DateTime Time { get; init; }
    public required Transponder Transponder { get; init; }
    public required TimingPoint TimingPoint { get; init; }
    public bool LowBattery { get; init; } = false;
    public bool LowStrength { get; init; } = false;
    public bool LowHits { get; init; } = false;
}
