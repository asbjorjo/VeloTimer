using System.Collections.Generic;

namespace VeloTimer.Shared.Models
{
    public class TimingLoop : Entity
    {   
        public long LoopId { get; set; }
        public long TrackId { get; set; }
        public double Distance { get; set; }
        public string Description { get; set; }

        public Track Track { get; set; }
        public List<Passing> Passings { get; set; }
    }
}