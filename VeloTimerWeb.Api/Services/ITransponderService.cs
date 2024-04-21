using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Data;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Data.Parameters;
using VeloTimerWeb.Api.Models.Riders;
using VeloTimerWeb.Api.Models.Statistics;
using VeloTimerWeb.Api.Models.Timing;
using static VeloTimer.Shared.Data.Models.Timing.TransponderType;

namespace VeloTimerWeb.Api.Services
{
    public interface ITransponderService
    {
        Task<Transponder> FindOrRegister(TimingSystem timingSystem, string systemId);
        Task<PaginatedList<Transponder>> GetAll(PaginationParameters pagination);
        Task<PaginatedList<TransponderOwnership>> GetTransponderOwnershipAsync(PaginationParameters pagination);
        Task<int> GetActiveCount(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<IEnumerable<double>> GetFastest(Transponder transponder, StatisticsItem statisticsItem, DateTimeOffset fromtime, DateTimeOffset? totime, int Count = 10);
        Task<IEnumerable<SegmentTime>> GetFastestForOwner(Rider rider, StatisticsItem statisticsItem, DateTimeOffset fromtime, DateTimeOffset totime, int Count = 10);
        Task<PaginatedList<SegmentTime>> GetTimesForOwner(Rider rider, StatisticsItem statisticsItem, TimeParameters timeParameters, PaginationParameters pagingParameters, string orderby);
        Task<PaginatedList<SegmentTime>> GetTimesForOwner(Rider rider, ICollection<TrackStatisticsItem> statisticsItems, TimeParameters timeParameters, PaginationParameters paginationParameters, string orderby);
        Task<PaginatedList<SegmentTime>> GetTimesForOwner(Rider rider, TrackStatisticsItem statisticsItems, TimeParameters timeParameters, PaginationParameters paginationParameters, string orderby);
        Task<IEnumerable<SegmentTime>> GetTimesForOwner(Rider rider, StatisticsItem statisticsItem, DateTimeOffset FromTime, string OrderBy, int Count = 50, bool IncludeIntermediates = true);
        Task<IEnumerable<SegmentTime>> GetTimesForOwner(Rider rider, ICollection<TrackStatisticsItem> statisticsItems, DateTimeOffset FromTime, string OrderBy, int Count = 50, bool IncludeIntermediates = true);
        Task<IEnumerable<SegmentTime>> GetTimesForOwner(Rider rider, TrackStatisticsItem statisticsItems, DateTimeOffset FromTime, string OrderBy, int Count = 50, bool IncludeIntermediates = true);
    }
}
