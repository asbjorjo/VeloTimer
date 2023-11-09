namespace VeloTimer.Shared.Data.Models.TrackSetup
{
    public class TrackWeb
    {
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public double Length { get; set; } = 0;
        public IEnumerable<TrackLayoutWeb> Layouts { get; set; } = Enumerable.Empty<TrackLayoutWeb>();
    }

    public class TrackLayoutWeb
    {
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public double Length { get; set; } = 0;
        public TrackWeb Track { get; set; } = new TrackWeb();
    }
}
