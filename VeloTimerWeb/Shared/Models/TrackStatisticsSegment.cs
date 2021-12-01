using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class TrackStatisticsSegment
    {
        public long Id { get; set; }
        public int Order { get; set; }
        public TrackSegment Segment { get; set; }
        public TrackStatisticsItem Element { get; set; }
    }
}
