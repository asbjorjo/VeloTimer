using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Client.Components;

namespace VeloTimerWeb.Client.Services
{
    public interface IApiClient
    {
        Task<RiderWeb> GetRiderByUserId(string userId);
        Task RemoveTransponderRegistration(string owner, string label, DateTimeOffset from, DateTimeOffset until);
        Task DeleteRiderProfile(string userid);
        Task SaveRiderProfile(RiderWeb riderWeb);

        Task<int> GetActiveTransponderCount(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<int> GetActiveRiderCount(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<IEnumerable<Transponder>> GetActiveTransponders(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<IEnumerable<RiderWeb>> GetActiveRiders(DateTimeOffset fromtime, DateTimeOffset? totime);

        Task<IEnumerable<SegmentTime>> GetBestTimes(string StatsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10, string Rider = null, bool OnePerRider = false);
        Task<PaginatedResponse<SegmentTime>> GetTimes(StatisticsParameters statisticsParameters, TimeParameters timeParameters, PaginationParameters pagingParameters, string Rider = null);
        Task<IEnumerable<SegmentTime>> GetTimesForTransponder(string StatsItem, long TransponderId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 50);
        Task<IEnumerable<SegmentTime>> GetTimesForTransponders(string StatsItem, IEnumerable<long> TransponderIds, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 50);
        Task<Passing> GetLastPassing();

        Task<IEnumerable<SegmentDistance>> GetCount(string StatsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10);

        Task<IEnumerable<TrackSegment>> GetTrackSegments(string Track);
        Task<TrackStatisticsItem> GetStatisticsItem(string Label, string Track);
        Task<IEnumerable<TrackStatisticsItem>> GetStatisticsItems(string Track);

        Task<IEnumerable<TimingLoop>> GetTimingPoints(string Track);
    }
}
