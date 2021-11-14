using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class SegmentPassing
    {
        public long SegmentId { get; set; }
        public long PassingId { get; set; }

        public Segment Segment { get; set; }
        public Passing Passing { get; set; }
    }
}
