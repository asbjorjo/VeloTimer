using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class TrackSegment
    {
        public long Id { get; set; }
        public long StartId { get; set; }
        public long EndId { get; set; }
        public TimingLoop Start { get; set; }
        public TimingLoop End { get; set; }

        public double Length { get => End.Distance - Start.Distance; }
    }
}
