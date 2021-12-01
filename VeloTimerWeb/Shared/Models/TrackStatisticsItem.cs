using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class TrackStatisticsItem
    {
        public long Id { get; set; }

        public StatisticsItem StatisticsItem { get; set; }

        public ICollection<TrackStatisticsSegment> Segments { get; set; } = new List<TrackStatisticsSegment>();

        public TrackSegment Start { get => Segments.OrderBy(s => s.Order).Select(s => s.Segment).First(); }
        public TrackSegment End { get => Segments.OrderBy(s => s.Order).Select(s => s.Segment).Last(); }
        public IReadOnlyList<TrackSegment> Intermediates { get => Segments.OrderBy(s => s.Order).Select(s => s.Segment).Skip(1).SkipLast(1).ToList().AsReadOnly(); }
    }
}
