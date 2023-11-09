using VeloTimer.Shared.Data.Models.TrackSetup;

namespace VeloTimer.Shared.Data.Models.Timing
{
    public class PassingWeb
    {
        public DateTime Time { get; set; } = DateTime.MinValue;
        public TransponderWeb Transponder { get; set; } = new TransponderWeb();
        public TrackWeb Track { get; set; } = new TrackWeb();
        public string LoopDescription { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
    }
}
