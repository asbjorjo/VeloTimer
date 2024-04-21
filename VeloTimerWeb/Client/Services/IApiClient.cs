using MudBlazor;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Data.Models.Riders;
using VeloTimer.Shared.Data.Models.Statistics;
using VeloTimer.Shared.Data.Models.Timing;
using VeloTimer.Shared.Data.Models.TrackSetup;
using VeloTimer.Shared.Data.Parameters;
using VeloTimerWeb.Client.Components;

namespace VeloTimerWeb.Client.Services
{
    public interface IApiClient
    {
        Task<RiderWeb> GetRiderByUserId(string userId);
        Task<PaginatedResponse<RiderWeb>> GetRiders(PaginationParameters pagination);
        Task RemoveTransponderRegistration(TransponderOwnershipWeb transponderOwnership);
        Task ExtendRegistration(TransponderOwnershipWeb transponderOwnership, DateTimeOffset newEnd);
        Task DeleteRiderProfile(string userid);
        Task SaveRiderProfile(RiderWeb riderWeb);

        Task<PaginatedResponse<TransponderWeb>> GetTransponders(PaginationParameters pagination);
        Task<PaginatedResponse<TransponderOwnershipWeb>> GetTransponderOwners(PaginationParameters pagination);
        Task<int> GetActiveTransponderCount(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<int> GetActiveRiderCount(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<IEnumerable<TransponderWeb>> GetActiveTransponders(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<IEnumerable<RiderWeb>> GetActiveRiders(DateTimeOffset fromtime, DateTimeOffset? totime);

        Task<IEnumerable<SegmentTime>> GetBestTimes(string StatsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10, string Rider = null, bool OnePerRider = false);
        Task<IEnumerable<SegmentTime>> GetTimes(StatisticsParameters statisticsParameters, DateTimeOffset? FromTime, int Count = 10, string Rider = null, bool IncludeIntermediate = true);
        Task<PaginatedResponse<SegmentTime>> GetTimes(StatisticsParameters statisticsParameters, TimeParameters timeParameters, PaginationParameters pagingParameters, string Rider = null);
        Task<IEnumerable<SegmentTime>> GetTimesForTransponder(string StatsItem, long TransponderId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 50);
        Task<IEnumerable<SegmentTime>> GetTimesForTransponders(string StatsItem, IEnumerable<long> TransponderIds, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 50);
        Task<PassingWeb> GetLastPassing();

        Task<IEnumerable<SegmentDistance>> GetCount(string StatsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10);

        Task<IEnumerable<TrackSegmentWeb>> GetTrackSegments(string Track);
        Task<TrackStatisticsItemWeb> GetStatisticsItem(string Label, string Track);
        Task<IEnumerable<TrackStatisticsItemWeb>> GetStatisticsItems(string Track);

        Task<IEnumerable<TimingLoopWeb>> GetTimingPoints(string Track);

        Task<AdminDashboardModel> GetAdminDashboardModel();
    }
}
