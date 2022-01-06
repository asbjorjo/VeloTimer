using System;
using System.Collections.Generic;
using System.Linq;

namespace VeloTimerWeb.Api.Models
{
    public class TrackSegmentPassing
    {
        public TrackSegmentPassing() { }

        public long Id { get; set; }
        public TrackSegment TrackSegment { get; private set; }
        public Transponder Transponder { get; private set; }
        public double Time { get; private set; }
        public double Speed { get; private set; }
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }

        public Passing Start { get; private set; }
        public Passing End { get; private set; }

        public static TrackSegmentPassing Create(TrackSegment segment, Passing start, Passing end)
        {
            TrackSegmentPassing passing = null;

            if (segment.Start == start.Loop && segment.End == end.Loop)
            {
                passing = new TrackSegmentPassing
                {
                    TrackSegment = segment,
                    Transponder = start.Transponder,
                    Start = start,
                    End = end,
                    StartTime = start.Time,
                    EndTime = end.Time,
                    Time = (end.Time - start.Time).TotalSeconds
                };
                passing.Speed = segment.Length / passing.Time;
            }

            return passing;
        }
    }

    public class TrackSectorSegmentPassing
    {
        public TrackSectorPassing SectorPassing { get; private set; }
        public TrackSegmentPassing SegmentPassing { get; private set; }

        public static TrackSectorSegmentPassing Create(TrackSectorPassing sectorPassing, TrackSegmentPassing segmentPassing)
        {
            TrackSectorSegmentPassing passing = new()
            {
                SectorPassing = sectorPassing,
                SegmentPassing = segmentPassing
            };

            return passing;
        }
    }

    public class TrackSectorPassing
    {
        public long Id { get; set; }
        public TrackSector TrackSector { get; private set; }
        public Transponder Transponder { get; private set; }
        public double Time { get; private set; }
        public double Speed { get; private set; }
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }
        public ICollection<TrackSectorSegmentPassing> SegmentPassings { get; private set; } = new List<TrackSectorSegmentPassing>();
        public ICollection<TrackLayoutPassing> LayoutPassings { get; private set; } = new List<TrackLayoutPassing>();

        public static TrackSectorPassing Create(TrackSector sector, Transponder transponder, IEnumerable<TrackSegmentPassing> passings)
        {
            TrackSectorPassing passing = null;

            if (sector.Segments.Count > passings.Count())
            {
                return null;
            }
            var orderedPassings = passings.OrderBy(x => x.EndTime);

            var continuous = orderedPassings.Select(x => x.TrackSegment.Id).SequenceEqual(sector.Segments.OrderBy(x => x.Order).Select(x => x.Segment.Id));

            var previous = orderedPassings.First();
            foreach (var segmentpassing in orderedPassings.Skip(1))
            {
                if (previous.EndTime != segmentpassing.StartTime) continuous = false;
                previous = segmentpassing;
            }

            if (continuous)
            {
                passing = new TrackSectorPassing
                {
                    TrackSector = sector,
                    Transponder = transponder,
                    Time = orderedPassings.Sum(x => x.Time),
                    StartTime = orderedPassings.First().StartTime,
                    EndTime = orderedPassings.Last().EndTime
                };
                passing.Speed = sector.Length / passing.Time;

                passing.SegmentPassings = orderedPassings.Select(x => TrackSectorSegmentPassing.Create(passing, x)).ToList();
            }

            return passing;
        }
    }
}
