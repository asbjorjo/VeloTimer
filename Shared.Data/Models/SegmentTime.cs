using System;
using System.Collections.Generic;
using System.Linq;

namespace VeloTimer.Shared.Data.Models
{
    public class SegmentTime
    {
        public string Rider { get; set; }
        public DateTimeOffset PassingTime { get; set; }
        public double Time { get; set; }
        public double Speed { get; set; }
        public bool ShowIntermediate { get; set; } = false;
        public IEnumerable<Intermediate> Intermediates { get; set; } = Enumerable.Empty<Intermediate>();
    }

    public class Intermediate
    {
        public double Time { get; set; }
        public double Speed { get; set; }
    }
}