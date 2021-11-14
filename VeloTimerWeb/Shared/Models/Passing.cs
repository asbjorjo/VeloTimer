using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace VeloTimer.Shared.Models
{
    public class Passing : Entity
    {
        public long TransponderId { get; set; }
        [JsonIgnore]
        public Transponder Transponder { get; set; }
        public long LoopId { get; set; }
        [JsonIgnore]
        public TimingLoop Loop { get; set; }
        public DateTimeOffset Time { get; set; }
        [Required]
        public string Source { get; set; }
    }
}
