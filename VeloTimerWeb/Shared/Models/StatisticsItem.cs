using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class StatisticsItem
    {
        public long Id { get; set; }

        public string Label { get; set; }
        public double Distance { get; set; }

        public ICollection<TrackStatisticsItem> Elements { get; private set; } = new List<TrackStatisticsItem>();
    }
}
