namespace VeloTimer.Shared.Models.Timing
{
    public class TransponderWeb
    {
        public string TimingSystem { get; set; }
        public string Label { get; set; }
        public string SystemId { get; set; }
        public PassingWeb LastSeen { get; set; }
    }
}
