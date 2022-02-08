using VeloTimer.Shared.Data.Models.Riders;

namespace VeloTimer.Shared.Data.Models.Statistics
{
    public class ActivityWeb
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public List<SessionWeb> Sessions { get; set; } = new List<SessionWeb>();
        public RiderWeb? Rider { get; set; }
    }
}
