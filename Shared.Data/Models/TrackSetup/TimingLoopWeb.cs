namespace VeloTimer.Shared.Data.Models.TrackSetup
{
    public class TimingLoopWeb
    {
        public int LoopId { get; set; }
        public double Distance { get; set; }
        public string Description { get; set; }

        public TrackWeb Track { get; set; }
    }
}
