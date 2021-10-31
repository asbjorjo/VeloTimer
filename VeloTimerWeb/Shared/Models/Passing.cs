using System;
using System.ComponentModel.DataAnnotations;

namespace VeloTimer.Shared.Models
{
    public class Passing : Entity
    {
        public long TransponderId { get; set; }
        public Transponder Transponder { get; set; }
        public long LoopId { get; set; }
        public TimingLoop Loop { get; set; }
        public DateTimeOffset Time { get; set; }
        [Required]
        public string Source { get; set; }
    }
}
