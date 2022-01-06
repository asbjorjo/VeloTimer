using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class TimingLoopWeb
    {
        public int LoopId { get; set; }
        public double Distance { get; set; }
        public string Description { get; set; }

        public TrackWeb Track { get; set; }
    }
}
