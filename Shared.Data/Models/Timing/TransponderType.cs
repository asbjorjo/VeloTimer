namespace VeloTimer.Shared.Data.Models.Timing
{
    public class TransponderType
    {
        public enum TimingSystem
        {
            Mylaps_X2
        }

        public TimingSystem System { get; set; }
    }
}
