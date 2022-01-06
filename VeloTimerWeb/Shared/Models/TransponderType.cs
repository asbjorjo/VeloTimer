namespace VeloTimer.Shared.Models
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
