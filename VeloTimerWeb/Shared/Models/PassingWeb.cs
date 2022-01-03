using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class PassingWeb
    {
        public DateTime Time { get; set; }
        public TransponderWeb Transponder { get; set; }
        public TrackWeb Track { get; set; }
        public string LoopName { get; set; }
        public string SourceId { get; set; }
    }
}
