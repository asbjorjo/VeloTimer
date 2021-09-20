using System;

namespace VeloTimerWeb.Shared.Models
{
    public class Passing : Entity
    {
        public long TransponderId { get; set; }
        public Transponder Transponder { get; set; }
        public long LoopId { get; set; }
        public TimingLoop Loop { get; set; }
        public DateTime Time { get; set; }
    }
}
