using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.PassingLoader.Contracts;

public record TrackPassingObserved
{
    public string Track {  get; init; } = string.Empty;
    public DateTimeOffset Time { get; init; }
    public string Transponder { get; init; } = string.Empty;
    public TransponderType.TimingSystem TimingSystem { get; init; }
    public long PassingPoint { get; init; }
    public string Source { get; init; } = string.Empty;
    public bool LowBattery { get; init; }
    public bool LowSignal { get; init; }
}
