using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class SegmentRun : Entity
    {
        public long SegmentId { get; set; }
        public long StartId { get; set; }
        public long EndId { get; set; }

        public double Time { get; set; }

        public Segment Segment { get; set; }
        public Passing Start { get; set; }
        public Passing End { get; set; }
    }
}
