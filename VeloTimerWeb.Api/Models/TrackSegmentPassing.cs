using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class TrackSegmentPassing
    {
        public TrackSegmentPassing() { }
        
        public long Id { get; set; }
        public TrackSegment TrackSegment { get; private set; }
        public Transponder Transponder { get; private set; }
        public double Time { get; private set; }
        public double Speed => TrackSegment.Length / Time;
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }

        public Passing Start { get; private set; }
        public Passing End { get; private set; }

        public static TrackSegmentPassing Create (TrackSegment segment, Passing start, Passing end)
        {
            var transponderPassing = new TrackSegmentPassing();

            transponderPassing.Start = start;
            transponderPassing.End = end;
            transponderPassing.TrackSegment = segment;
            transponderPassing.Transponder = start.Transponder;
            transponderPassing.StartTime = start.Time;
            transponderPassing.EndTime = end.Time;
            transponderPassing.Time = (end.Time - start.Time).TotalSeconds;

            return transponderPassing;
        }
    }
}
