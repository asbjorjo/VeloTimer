using System.Collections.Generic;

namespace VeloTimer.Shared.Models
{
    public class TimingLoop
    {   
        public long Id { get; set; }
        public int LoopId { get; set; }
        public long TrackId { get; set; }
        
        public double Distance { get; set; }
        public string Description { get; set; }

        public Track Track { get; set; }
        public IEnumerable<Passing> Passings { get; set; }
    }
}