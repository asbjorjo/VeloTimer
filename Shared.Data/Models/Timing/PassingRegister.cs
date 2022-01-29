namespace VeloTimer.Shared.Data.Models.Timing
{
    public class PassingRegister
    {
        public string Track { get; set; }
        public string TransponderId { get; set; }
        public TransponderType.TimingSystem TimingSystem { get; set; }
        public long LoopId { get; set; }
        public DateTimeOffset Time { get; set; }
        public string Source { get; set; }
        public bool LowBattery { get; set; }
    }
}
