using System;
using System.Collections.Generic;
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

        public virtual ICollection<TimingLoop> Intermediates { get; set; }
    }
}
