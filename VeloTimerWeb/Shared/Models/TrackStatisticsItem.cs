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
        public TrackLayout Layout { get; set; }
        public int Laps { get; set; } = 0;
        public double MinTime { get; set; } = 0;
        public double MaxTime { get; set; } = double.MaxValue;
        
        public static TrackStatisticsItem Create(StatisticsItem statistics, TrackLayout layout, int laps)
        {
            TrackStatisticsItem item = null;

            var distance = layout.Segments.Sum(x => x.Segment.Length) * laps;

            if ((!statistics.IsLapCounter && distance == statistics.Distance) || (statistics.IsLapCounter && laps == 1))
            {
                item = new TrackStatisticsItem
                {
                    StatisticsItem = statistics,
                    Layout = layout,
                    Laps = laps,
                };
            }

            return item;
        }
    }
}
