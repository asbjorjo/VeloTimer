using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Models.Statistics;

namespace VeloTimerWeb.Api.Services
{
    public interface IStatisticsService
    {
        Task<StatisticsItem> GetItemBySlug(string slug);
        Task<IEnumerable<TrackStatisticsItem>> GetTrackItemsBySlugs(string track, string item = "");
        Task<TrackStatisticsItem> GetTrackItemBySlugs(string item, string track, string layout);
        Task<IEnumerable<SegmentTime>> GetBestTimes(StatisticsItem StatisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10);
        Task<IEnumerable<KeyValuePair<string, double>>> GetTopDistances(StatisticsItem StatisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10);
    }
}
