using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace VeloTimer.Shared.Models
{
    public class Passing
    {
        public long Id { get; set; }
        public Transponder Transponder { get; set; }
        public TimingLoop Loop { get; set; }
        public DateTime Time { get; set; }
        [Required]
        public string SourceId { get; set; }

        public long TransponderId { get; set; }
        public long LoopId { get; set; }
    }
}
