using System;
using System.Collections.Generic;

namespace VeloTimer.Shared.Models
{
    public class SegmentTime
    {
        public String Rider { get; set; }
        public DateTimeOffset PassingTime { get; set; }
        public double Time { get; set; }
        public double Speed { get; set; }
    }
}