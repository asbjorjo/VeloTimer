using VeloTime.Storage.Models.Riders;
using VeloTime.Storage.Models.Statistics;
using VeloTime.Storage.Models.Timing;
using VeloTimer.Shared.Data;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Data.Parameters;
using static VeloTimer.Shared.Data.Models.Timing.TransponderType;

namespace VeloTime.Services
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
    }
}
