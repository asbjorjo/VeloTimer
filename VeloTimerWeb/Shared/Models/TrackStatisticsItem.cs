using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class TrackStatisticsItem
    {
        public long Id { get; private set; }

        public StatisticsItem StatisticsItem { get; private set; }

        public TrackLayout Layout { get; private set; }

        public int Laps { get; private set; } = 0;

        public static TrackStatisticsItem Create(StatisticsItem statistics, TrackLayout layout, int laps)
        {
            var distance = layout.Segments.Sum(x => x.Segment.Length) * laps;
            if (distance != statistics.Distance)
            {
                return null;
            }

            var item = new TrackStatisticsItem
            {
                StatisticsItem = statistics,
                Layout = layout,
                Laps = laps
            };

            return item;
        }
    }
}
