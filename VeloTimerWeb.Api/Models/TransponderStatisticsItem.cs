using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class TransponderStatisticsItem
    {
        public long Id { get; set; }
        public TrackStatisticsItem StatisticsItem { get; set; }
        private List<TransponderStatisticsSegment> segmentpassinglist { get; set; } = new();

        public Transponder Transponder { get; private set; }
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }
        public double Time { get; private set; }

        public IReadOnlyCollection<TrackSegmentPassing> SegmentPassings => segmentpassinglist.Select(x => x.SegmentPassing).OrderBy(x => x.StartTime).ToList().AsReadOnly();

        public static object Create(TrackStatisticsItem statisticsItem, Transponder transponder, List<TrackSegmentPassing> trackSegmentPassings)
        {
            var item = new TransponderStatisticsItem();
            item.StatisticsItem = statisticsItem;
            foreach (var segment in trackSegmentPassings)
            {
                item.segmentpassinglist.Add(new TransponderStatisticsSegment { SegmentPassing = segment });
            }

            item.Transponder = transponder;
            item.Time = trackSegmentPassings.Sum(x => x.Time);
            item.StartTime = trackSegmentPassings.First().StartTime;
            item.EndTime = trackSegmentPassings.Last().EndTime;

            return item;
        }
    }

    public class TransponderStatisticsSegment
    {
        public TransponderStatisticsItem TransponderStatisticsItem { get; set; }
        public TrackSegmentPassing SegmentPassing { get; set; }
    }
}
