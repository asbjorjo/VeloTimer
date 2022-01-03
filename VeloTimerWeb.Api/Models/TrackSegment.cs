using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Models
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

        public TrackSegmentWeb ToWeb()
        {
            return new TrackSegmentWeb
            {
                End = End.ToWeb(),
                Start = Start.ToWeb(),
                Length = Length
            };
        }
    }

    public class TrackSector
    {
        public long Id { get; private set; }
        public ICollection<TrackSectorSegment> Segments { get; private set; } = new List<TrackSectorSegment>();

        public double Length { get; private set; }

        public static TrackSector Create(IOrderedEnumerable<TrackSegment> segments)
        {
            var sector = new TrackSector();
            
            int order = 1;
            foreach (var segment in segments)
            {
                sector.Segments.Add(TrackSectorSegment.Create(sector, segment, order));
                order++;
            }
            sector.Length = sector.Segments.Sum(x => x.Segment.Length);

            return sector;
        }
    }

    public class TrackSectorSegment
    {
        public TrackSector Sector { get; private set; }
        public TrackSegment Segment { get; private set; }
        public int Order { get; private set; }

        public static TrackSectorSegment Create(TrackSector sector, TrackSegment segment, int order)
        {
            var sectorsegment = new TrackSectorSegment
            {
                Sector = sector,
                Segment = segment,
                Order = order
            };
            return sectorsegment;
        }
    }
}
