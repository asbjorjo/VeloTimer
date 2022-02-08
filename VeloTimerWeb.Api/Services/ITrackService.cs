using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTime.Storage.Models.Statistics;
using VeloTime.Storage.Models.TrackSetup;
using VeloTimer.Shared.Data;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Data.Parameters;

namespace VeloTimerWeb.Api.Services
{
    public interface ITrackService
    {
        Task<Track> GetTrackBySlug(string slug);
        Task<IEnumerable<SegmentDistance>> GetCount(TrackStatisticsItem statisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10);
        Task<IEnumerable<SegmentTime>> GetFastest(IEnumerable<TrackStatisticsItem> statisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10);
        Task<IEnumerable<SegmentTime>> GetFastest(Track Track, string Label, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10);
        Task<IEnumerable<SegmentTime>> GetRecent(TrackStatisticsItem statisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 50);
        Task<PaginatedList<SegmentTime>> GetRecent(IEnumerable<TrackStatisticsItem> statisticsItems, TimeParameters time, PaginationParameters pagination, string orderby);
    }
}
