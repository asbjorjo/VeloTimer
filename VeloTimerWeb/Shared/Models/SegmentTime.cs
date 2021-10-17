using System;
using System.Collections.Generic;

namespace VeloTimer.Shared.Models
{
    public class SegmentTimeRider : SegmentTime
    {
        public string Rider { get; set; }

        public ICollection<SegmentTime> Intermediates { get; set; } = new List<SegmentTime>();
    }
    public class SegmentTime
    {
        public DateTime PassingTime { get; set; }
        public long Loop { get; set; }
        public double Segmentlength { get; set; }
        public double Segmenttime { get; set; }
        public double Segmentspeed => Segmentlength / Segmenttime * 3.6;
    }
}