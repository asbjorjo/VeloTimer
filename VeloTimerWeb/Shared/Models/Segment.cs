using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class Segment : Entity
    {
        public string Label { get; set; }
        public long StartId { get; set; }
        public long EndId { get; set; }

        public bool DisplayIntermediates { get; set; }
        public bool RequireIntermediates { get; set; }

        public long MinTime { get; set; } = 0;
        public long MaxTime { get; set; } = long.MaxValue;

        public TimingLoop Start { get; set; }
        public TimingLoop End { get; set; }

        public virtual ICollection<Intermediate> Intermediates { get; set; } = new List<Intermediate>();
    }

    public class Intermediate
    {
        public long SegmentId { get; set; }
        public long LoopId { get; set; }

        [JsonIgnore]
        public Segment Segment { get; set; }
        public TimingLoop Loop { get; set; }
    }
}
