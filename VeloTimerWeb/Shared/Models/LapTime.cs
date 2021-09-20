using System;

namespace VeloTimerWeb.Shared.Models
{
    public class LapTime
    {
        public string Rider { get; set; }
        public DateTime PassingTime { get; set; }
        public int Laplength { get; set; }
        public double Laptime { get; set; }
        public double Lapspeed => Laplength / Laptime * 3.6;
    }
}
