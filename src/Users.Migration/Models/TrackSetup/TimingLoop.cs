using System.Collections.Generic;
using VeloTime.Users.Migration.Models.Timing;

namespace VeloTime.Users.Migration.Models.TrackSetup
{
    public class TimingLoop
    {
        public long Id { get; private set; }
        public long TrackId { get; private set; }

        public int LoopId { get; set; }
        public double Distance { get; set; }
        public string Description { get; set; }

        public Track Track { get; set; }
        public IEnumerable<Passing> Passings { get; set; } = new List<Passing>();
    }
}
