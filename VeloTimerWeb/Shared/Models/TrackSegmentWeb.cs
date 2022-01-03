using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class TrackSegmentWeb
    {
        public TimingLoopWeb Start { get; set; }
        public TimingLoopWeb End { get; set; }

        public double Length { get; set; }
    }
}
