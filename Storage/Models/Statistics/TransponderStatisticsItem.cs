using VeloTime.Storage.Models.Timing;
using VeloTime.Storage.Models.TrackSetup;

namespace VeloTime.Storage.Models.Statistics
{
    public class TransponderStatisticsItem
    {
        public long Id { get; set; }
        public TrackStatisticsItem StatisticsItem { get; private set; }
        public List<TransponderStatisticsLayout> LayoutPassingList { get; private set; } = new();

        public Transponder Transponder { get; private set; }
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


            return item;
        }
    }

    public class TransponderStatisticsLayout
    {
        public TransponderStatisticsItem TransponderStatisticsItem { get; set; }
        public TrackLayoutPassing LayoutPassing { get; set; }
    }
}
