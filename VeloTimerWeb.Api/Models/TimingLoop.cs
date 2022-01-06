using System.Collections.Generic;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Models;

namespace VeloTimerWeb.Api.Models
{
    public class TimingLoop
    {   
        public long Id { get; private set; }
        public long TrackId { get; private set; }

        public int LoopId { get; set; }
        public double Distance { get; set; }
        public string Description { get; set; }

        public Track Track { get; set; }
        public IEnumerable<Passing> Passings { get; set; }
    }
}