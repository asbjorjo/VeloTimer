namespace VeloTimer.Shared.Data.Models.Timing
{
    public class TransponderWeb
    {
        public string TimingSystem { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string SystemId { get; set; } = string.Empty;
        public PassingWeb? LastSeen { get; set; }
    }
}
