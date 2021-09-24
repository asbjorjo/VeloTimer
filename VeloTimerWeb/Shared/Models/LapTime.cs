using System;

namespace VeloTimer.Shared.Models
{
    public class LapTime
    {
        public string Rider { get; set; }
        public DateTime PassingTime { get; set; }
        public double Laplength { get; set; }
        public double Laptime { get; set; }
        public double Lapspeed => Laplength / Laptime * 3.6;
    }
}
