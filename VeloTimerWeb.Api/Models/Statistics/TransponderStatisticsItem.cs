using System;
using System.Collections.Generic;
using System.Linq;
using VeloTimerWeb.Api.Models.Riders;
using VeloTimerWeb.Api.Models.Timing;
using VeloTimerWeb.Api.Models.TrackSetup;

namespace VeloTimerWeb.Api.Models.Statistics
{
    public class TransponderStatisticsItem
    {
        public long Id { get; set; }
        public TrackStatisticsItem StatisticsItem { get; private set; }
        public List<TransponderStatisticsLayout> LayoutPassingList { get; private set; } = new();

        public Transponder Transponder { get; private set; }
        public Rider Rider {  get; private set; }
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }
        public double Time { get; private set; }
        public double Speed { get; private set; }

        public IReadOnlyCollection<TrackLayoutPassing> LayoutPassings => LayoutPassingList.Select(x => x.LayoutPassing).OrderBy(x => x.EndTime).ToList().AsReadOnly();

        public static TransponderStatisticsItem Create(TrackStatisticsItem statisticsItem, Transponder transponder, IEnumerable<TrackLayoutPassing> passings)
        {
            var item = new TransponderStatisticsItem
            {
                StatisticsItem = statisticsItem
            };
            foreach (var layout in passings)
            {
                item.LayoutPassingList.Add(new TransponderStatisticsLayout { LayoutPassing = layout });
            }

            item.Transponder = transponder;
            item.Time = passings.Sum(x => x.Time);
            item.StartTime = passings.First().StartTime;
            item.EndTime = passings.Last().EndTime;
            item.Speed = passings.Average(x => x.Speed);
            item.Rider = transponder.Owners.Where(o => o.OwnedFrom < item.StartTime && o.OwnedUntil >= item.EndTime).Select(o => o.Owner).FirstOrDefault();

            return item;
        }
    }

    public class TransponderStatisticsLayout
    {
        public TransponderStatisticsItem TransponderStatisticsItem { get; set; }
        public TrackLayoutPassing LayoutPassing { get; set; }
    }
}
