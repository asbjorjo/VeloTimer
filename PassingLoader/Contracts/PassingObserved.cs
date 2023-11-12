using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.PassingLoader.Contracts
{
    public record PassingObserved
    {
        public DateTimeOffset Time { get; init; }
        public string Transponder { get; init; } = string.Empty;
        public TransponderType.TimingSystem TimingSystem { get; init; }
        public long PassingPoint { get; init; }
        public string Source { get; init; } = string.Empty;
    }
}
