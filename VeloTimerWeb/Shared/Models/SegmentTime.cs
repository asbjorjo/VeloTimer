using System;
using System.Collections.Generic;

namespace VeloTimer.Shared.Models
{
    public class SegmentTime
    {
        public string Rider { get; set; }
        public DateTimeOffset PassingTime { get; set; }
        public double Time { get; set; }
        public double Speed { get; set; }
        public IEnumerable<Intermediate> Intermediates { get; set; }
    }

    public class Intermediate
    {
        public double Time { get; set; }
        public double Speed { get; set; }
    }
}