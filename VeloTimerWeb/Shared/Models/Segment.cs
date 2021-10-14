using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class Segment : Entity
    {
        public string Label { get; set; }
        public long StartId { get; set; }
        public long EndId { get; set; }

        public TimingLoop Start { get; set; }
        public TimingLoop End { get; set; }

        public virtual ICollection<Intermediate> Intermediates { get; set; }
    }

    public class Intermediate
    {
        public long SegmentId { get; set; }
        public long LoopId { get; set; }

        public Segment Segment { get; set; }
        public TimingLoop Loop { get; set; }
    }
}
