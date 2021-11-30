using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class TrackSegment
    {
        public TrackSegment()
        {
        }

        public TrackSegment(TimingLoop Start, TimingLoop End)
        {
            this.Start = Start;
            this.End = End;

            Length = End.Distance > Start.Distance ? End.Distance-Start.Distance : End.Distance - Start.Distance + Start.Track.Length;
        }

        public long Id { get; private set; }
        public long StartId { get; private set; }
        public long EndId { get; private set; }
        public TimingLoop Start { get; private set; }
        public TimingLoop End { get; private set; }

        public double Length { get; private set; }
    }
}
