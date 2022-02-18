using VeloTime.Storage.Models.Statistics;
using VeloTime.Storage.Models.Timing;
using VeloTimer.Shared.Data.Models;

namespace VeloTime.Services
{
    public interface IStatisticsService
    {
        Task<Activity> CreateOrUpdateActivity(Passing passing);
        Task<StatisticsItem> GetItemBySlug(string slug);
        Task<IEnumerable<TrackStatisticsItem>> GetTrackItemsBySlugs(string track, string item = "");
        Task<TrackStatisticsItem> GetTrackItemBySlugs(string item, string track, string layout);
        Task<IEnumerable<SegmentTime>> GetBestTimes(StatisticsItem StatisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10);
        Task<IEnumerable<KeyValuePair<string, double>>> GetTopDistances(StatisticsItem StatisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10);
    }
}
