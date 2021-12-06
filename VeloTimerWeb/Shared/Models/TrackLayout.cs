using System;
using System.Collections.Generic;
using System.Linq;

namespace VeloTimer.Shared.Models
{
    public class TrackLayout
    {
        public long Id { get; private set; }
        public string Name { get; set; }
        public Track Track { get; private set; }
        public ICollection<TrackLayoutSegment> Segments { get; private set; } = new List<TrackLayoutSegment>();

        public static TrackLayout Create(Track track, string name, IOrderedEnumerable<TrackSegment> segments)
        {
            TrackLayout trackLayout = new()
            {
                Name = name,
                Track = track
            };

            int order = 1;
            foreach(var segment in segments)
            {
                var layoutSegment = TrackLayoutSegment.Create(trackLayout, segment, order);
                trackLayout.Segments.Add(layoutSegment);
                order++;
            }

            return trackLayout;
        }
    }

    public class TrackLayoutSegment
    {
        public long Id { get; private set; }
        public TrackLayout Layout { get; private set; }
        public TrackSegment Segment { get; private set; }
        public int Order { get; private set; }

        public static TrackLayoutSegment Create(TrackLayout layout, TrackSegment segment, int order)
        {
            var layoutsegment = new TrackLayoutSegment
            {
                Layout = layout,
                Segment = segment,
                Order = order
            };

            return layoutsegment;
        }
    }

    public class TrackLayoutSegmentPassing
    {
        public long Id { get; private set; }
        public TrackLayoutSegment Segment { get; private set; }
        public Passing Passing { get; private set; }

    }

    public class TrackLayoutPassing
    {
        public long Id { get; private set; }
        public TrackLayout TrackLayout { get; private set; }
        public Transponder Transponder { get; private set; }
        public ICollection<TrackSegmentPassing> Passings { get; private set; } = new List<TrackSegmentPassing>();

        public double Time { get; private set; }
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }

        public static TrackLayoutPassing Create(TrackLayout layout, Transponder transponder, IEnumerable<TrackSegmentPassing> passings)
        {
            var orderedPassings = passings.OrderBy(x => x.StartTime);

            var layoutPassing = new TrackLayoutPassing
            {
                TrackLayout = layout,
                Transponder = transponder,
                Passings = passings.ToList(),
                StartTime = orderedPassings.First().StartTime,
                EndTime = orderedPassings.Last().EndTime
            };
            layoutPassing.Time = (layoutPassing.EndTime - layoutPassing.StartTime).TotalSeconds;

            return layoutPassing;
        }
    }
}
