using System;

namespace VeloTimer.Shared.Models
{
    public class SegmentTimeRider : SegmentTime
    {
        public string Rider { get; set; }
    }
    public class SegmentTime
    {
        public DateTime PassingTime { get; set; }
        public double Segmentlength { get; set; }
        public double Segmenttime { get; set; }
        public double Segmentspeed => Segmentlength / Segmenttime * 3.6;
    }
}