using Slugify;

namespace VeloTimerWeb.Api.Models.Statistics
{
    public class StatisticsItem
    {
        public long Id { get; set; }

        public string Label { get; set; }
        public double Distance { get; set; }
        public bool IsLapCounter { get; set; } = false;
        public string Slug { get; set; }

        public static StatisticsItem Create(string label, double distance, bool isLapCounter)
        {
            var slugHelper = new SlugHelper();

            StatisticsItem item = new StatisticsItem
            {
                Label = label,
                Distance = distance,
                IsLapCounter = isLapCounter,
                Slug = slugHelper.GenerateSlug(label)
            };

            return item;
        }
    }
}
