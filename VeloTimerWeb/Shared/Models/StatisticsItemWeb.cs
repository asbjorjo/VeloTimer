namespace VeloTimer.Shared.Models
{
    public class StatisticsItemWeb
    {
        public string Label { get; set; }
        public string Slug { get; set; }
        public double Distance { get; set; }
        public bool IsLapCounter { get; set; }
    }

    public class TrackStatisticsItemWeb
    {
        public StatisticsItemWeb StatisticsItem { get; set; }
        public TrackLayoutWeb Layout { get; set; }
        public int Laps { get; set; }
        public double MinTime { get; set; }
        public double MaxTime { get; set; }
    }
}
