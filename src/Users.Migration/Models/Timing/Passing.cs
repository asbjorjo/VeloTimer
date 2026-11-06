using System;
using VeloTime.Users.Migration.Models.TrackSetup;

namespace VeloTime.Users.Migration.Models.Timing
{
    public class Passing
    {
        public long Id { get; private set; }
        public Transponder Transponder { get; set; }
        public TimingLoop Loop { get; set; }
        public DateTime Time { get; set; }
        public string SourceId { get; set; }

        public long TransponderId { get; private set; }
        public long LoopId { get; private set; }
    }
}
