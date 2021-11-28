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

        public IReadOnlyCollection<TrackSegmentPassing> SegmentPassings => segmentpassinglist.Select(x => x.SegmentPassing).ToList().AsReadOnly();

        public static object Create(TrackStatisticsItem statisticsItem, List<TrackSegmentPassing> trackSegmentPassings)
        {
            var item = new TransponderStatisticsItem();
            item.StatisticsItem = statisticsItem;
            foreach (var segment in trackSegmentPassings)
            {
                item.segmentpassinglist.Add(new TransponderStatisticsSegment { SegmentPassing = segment });
            }

            return item;
        }
    }

    public class TransponderStatisticsSegment
    {
        public TransponderStatisticsItem TransponderStatisticsItem { get; set; }
        public TrackSegmentPassing SegmentPassing { get; set; }
    }
}
