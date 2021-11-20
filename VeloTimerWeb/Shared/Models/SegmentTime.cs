using System;
using System.Collections.Generic;

namespace VeloTimer.Shared.Models
{
    public class SegmentTimeRider : SegmentTime
    {
        public string Rider { get; set; }

        public IEnumerable<SegmentTime> Intermediates { get; set; } = new List<SegmentTime>();
    }
    public class SegmentTime
    {
        public DateTimeOffset PassingTime { get; set; }
        public long Loop { get; set; }
        public double Segmentlength { get; set; }
        public double Segmenttime { get; set; }
        public double Segmentspeed => Segmentlength / Segmenttime * 3.6;
    }
}