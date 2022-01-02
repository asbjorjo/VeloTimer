using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace VeloTimer.Shared.Models
{
    public class Passing
    {
        public long Id { get; private set; }
        public Transponder Transponder { get; set; }
        public TimingLoop Loop { get; set; }
        public DateTime Time { get; set; }
        public string SourceId { get; set; }

        public long TransponderId { get; private set; }
        public long LoopId { get; private set; }

        public PassingWeb ToWeb()
        {
            return new PassingWeb
            {
                Time = Time,
                Transponder = Transponder.ToWeb(),
                Track = Loop?.Track?.ToWeb(),
                LoopName = Loop?.Description
            };
        }

        public PassingWeb ToWeb(TransponderWeb transponder)
        {
            return new PassingWeb
            {
                Time = Time,
                Transponder = transponder,
                Track = Loop?.Track?.ToWeb(),
                LoopName = Loop?.Description
            };
        }
    }
}
