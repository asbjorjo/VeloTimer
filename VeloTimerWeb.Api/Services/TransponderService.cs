using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Data;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Data.Models.Timing;
using VeloTimer.Shared.Data.Parameters;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.Riders;
using VeloTimerWeb.Api.Models.Statistics;
using VeloTimerWeb.Api.Models.Timing;
using VeloTimerWeb.Api.Models.TrackSetup;

namespace VeloTimerWeb.Api.Services
{
    public class TransponderService : ITransponderService
    {
        private readonly VeloTimerDbContext _context;
        private readonly ILogger<TransponderService> _logger;

        public TransponderService(VeloTimerDbContext context, ILogger<TransponderService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PaginatedList<Transponder>> GetAll(PaginationParameters pagination)
        {
            var query = _context.Set<Transponder>()
                .OrderByDescending(x => x.Passings.OrderByDescending(x => x.Time).FirstOrDefault());

            var transponders = await query.ToPaginatedListAsync(pagination.PageNumber, pagination.PageSize);

            transponders.ForEach(x => _context.Entry(x).Collection(x => x.Passings).Query().OrderByDescending(x => x.Time).Take(1).Include(x => x.Loop).ThenInclude(x => x.Track).Load());

            return transponders;
        }

        public async Task<PaginatedList<TransponderOwnership>> GetTransponderOwnershipAsync(PaginationParameters pagination)
        {
            var query = _context.Set<TransponderOwnership>()
                .Include(x => x.Owner)
                .Include(x => x.Transponder);

            return await query.ToPaginatedListAsync(pagination.PageNumber, pagination.PageSize);
        }

        public async Task<int> GetActiveCount(DateTimeOffset FromTime, DateTimeOffset? ToTime)
        {
            var fromtime = FromTime.UtcDateTime;
            var totime = ToTime.HasValue ? ToTime.Value.UtcDateTime : DateTimeOffset.MaxValue.UtcDateTime;

            var active = await _context.Set<Passing>()
                .AsNoTracking()
                .Where(p => p.Time >= fromtime && p.Time < totime)
                .Select(p => p.TransponderId)
                .Distinct()
                .CountAsync();

            return active;
        }

        public async Task<IEnumerable<double>> GetFastest(Transponder transponder, StatisticsItem statisticsItem, DateTimeOffset fromtime, DateTimeOffset? totime, int Count)
        {
            totime ??= DateTimeOffset.MaxValue;

            var times = Enumerable.Empty<double>();

            var query = _context.Set<TransponderStatisticsItem>()
                .Where(x => x.StartTime >= fromtime && x.EndTime <= totime)
                .Where(p => p.Transponder == transponder)
                .Where(p => p.StatisticsItem.StatisticsItem == statisticsItem)
                .OrderBy(p => p.Time)
                .Select(x => x.Time)
                .Take(Count);

            _logger.LogDebug(query.ToQueryString());

            times = await query.ToListAsync();

            return times;
        }

        public async Task<IEnumerable<SegmentTime>> GetFastestForOwner(Rider rider, StatisticsItem statisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count)
        {
            var times = Enumerable.Empty<SegmentTime>();
            var fromtime = FromTime.UtcDateTime;
            var totime = ToTime.UtcDateTime;

            if (totime <= fromtime)
            {
                return times;
            }

            var query = from tsi in _context.Set<TransponderStatisticsItem>()
                        where
                            tsi.StatisticsItem.StatisticsItem == statisticsItem
                            && tsi.StartTime >= fromtime
                            && tsi.EndTime <= totime
                            && tsi.Rider == rider
                        orderby tsi.Time
                        select new SegmentTime
                        {
                            PassingTime = tsi.EndTime,
                            Speed = tsi.StatisticsItem.Layout.Distance * tsi.StatisticsItem.Laps / tsi.Time * 3.6,
                            Time = tsi.Time
                        };

            _logger.LogDebug(query.Take(Count).ToQueryString());

            times = await query.Take(Count).ToListAsync();

            return times;
        }

        public async Task<PaginatedList<SegmentTime>> GetTimesForOwner(Rider rider, StatisticsItem statisticsItem, TimeParameters timeParameters, PaginationParameters pagingParameters, string orderby)
        {
            orderby = orderby.Replace("endtime", "passingtime");

            var fromtime = timeParameters.FromTime;
            var totime = timeParameters.ToTime;

            if (totime <= fromtime)
            {
                return null;
            }

            var query = from tsi in _context.Set<TransponderStatisticsItem>()
                        where
                            tsi.StatisticsItem.StatisticsItem == statisticsItem
                            && tsi.StartTime >= fromtime
                            && tsi.EndTime <= totime
                            && tsi.Rider == rider
                        select new SegmentTime
                        {
                            PassingTime = tsi.EndTime,
                            Speed = tsi.Speed * 3.6,
                            Time = tsi.Time,
                            Intermediates = tsi.LayoutPassingList.SelectMany(x => x.LayoutPassing.Passings).Select(x => new Intermediate { Speed = x.Speed * 3.6, Time = x.Time })
                        };

            query = query.ApplySort(orderby);

            var times = await query.ToPaginatedListAsync(pagingParameters.PageNumber, pagingParameters.PageSize);

            return times;
        }

        public async Task<PaginatedList<SegmentTime>> GetTimesForOwner(Rider rider, TrackStatisticsItem statisticsItems, TimeParameters timeParameters, PaginationParameters paginationParameters, string orderby)
        {
            orderby = orderby.Replace("endtime", "passingtime");

            var fromtime = timeParameters.FromTime;
            var totime = timeParameters.ToTime;

            if (totime <= fromtime) return null;

            var query = from tsi in _context.Set<TransponderStatisticsItem>()
                        where
                            tsi.StatisticsItem == statisticsItems
                            && tsi.StartTime >= fromtime
                            && tsi.EndTime <= totime
                            && tsi.Rider == rider
                        select new SegmentTime
                        {
                            PassingTime = tsi.EndTime,
                            Speed = tsi.Speed * 3.6,
                            Time = tsi.Time,
                            Intermediates = tsi.LayoutPassingList.SelectMany(x => x.LayoutPassing.Passings).Select(x => new Intermediate { Speed = x.Speed * 3.6, Time = x.Time })
                        };

            query = query.ApplySort(orderby);

            var times = await query.ToPaginatedListAsync(paginationParameters.PageNumber, paginationParameters.PageSize);

            return times;
        }

        public async Task<PaginatedList<SegmentTime>> GetTimesForOwner(Rider rider, ICollection<TrackStatisticsItem> statisticsItems, TimeParameters timeParameters, PaginationParameters paginationParameters, string orderby)
        {
            orderby = orderby.Replace("endtime", "passingtime");

            var fromtime = timeParameters.FromTime;
            var totime = timeParameters.ToTime;

            if (totime <= fromtime) return null;

            var query = from tsi in _context.Set<TransponderStatisticsItem>()
                        where
                            statisticsItems.Contains(tsi.StatisticsItem)
                            && tsi.StartTime >= fromtime
                            && tsi.EndTime <= totime
                            && tsi.Rider == rider
                        select new SegmentTime
                        {
                            PassingTime = tsi.EndTime,
                            Speed = tsi.Speed * 3.6,
                            Time = tsi.Time,
                            Intermediates = tsi.LayoutPassingList.SelectMany(x => x.LayoutPassing.Passings).Select(x => new Intermediate { Speed = x.Speed * 3.6, Time = x.Time })
                        };

            query = query.ApplySort(orderby);

            var times = await query.ToPaginatedListAsync(paginationParameters.PageNumber, paginationParameters.PageSize);

            return times;
        }

        public async Task<Transponder> FindOrRegister(TransponderType.TimingSystem timingSystem, string systemId)
        {
            var existing = await _context.Set<Transponder>().FirstOrDefaultAsync(x => x.TimingSystem == timingSystem && x.SystemId == systemId);

            if (existing == null)
            {
                existing = new Transponder { SystemId = systemId, TimingSystem = timingSystem };
                _context.Add(existing);
                await _context.SaveChangesAsync();
            }

            return existing;
        }

        public async Task<IEnumerable<SegmentTime>> GetTimesForOwner(Rider rider, StatisticsItem statisticsItem, DateTimeOffset FromTime, string OrderBy, int Count = 50, bool IncludeIntermediates = true)
        {
            var statisticsItems = await _context.Set<TrackStatisticsItem>().Where(x => x.StatisticsItem == statisticsItem).ToListAsync();

            return await GetTimesForOwner(rider, statisticsItem, FromTime, OrderBy, Count, IncludeIntermediates);
        }

        public async Task<IEnumerable<SegmentTime>> GetTimesForOwner(Rider rider, ICollection<TrackStatisticsItem> statisticsItems, DateTimeOffset FromTime, string OrderBy, int Count = 50, bool IncludeIntermediates = true)
        {
            var times = Enumerable.Empty<SegmentTime>();

            var passings = await GetPassings(rider, statisticsItems, FromTime, OrderBy, Count);

            if (IncludeIntermediates)
            {
                var inters = await GetIntermediates(passings);

                times = passings.Select(x => new SegmentTime
                {
                    Rider = x.Rider.Name,
                    PassingTime = x.EndTime,
                    Time = x.Time,
                    Speed = x.Speed * 3.6,
                    Intermediates = inters[x.Id]
                });
            }
            else
            {
                times = passings.Select(x => new SegmentTime
                {
                    Rider = x.Rider.Name,
                    PassingTime = x.EndTime,
                    Time = x.Time,
                    Speed = x.Speed * 3.6
                });
            }

            return times;
        }

        public async Task<IEnumerable<SegmentTime>> GetTimesForOwner(Rider rider, TrackStatisticsItem statisticsItem, DateTimeOffset FromTime, string OrderBy, int Count = 50, bool IncludeIntermediates = true)
        {
            var statisticsItems = new List<TrackStatisticsItem>
            {
                statisticsItem
            };

            return await GetTimesForOwner(rider, statisticsItems, FromTime, OrderBy, Count, IncludeIntermediates);
        }

        private async Task<IEnumerable<TransponderStatisticsItem>> GetPassings(Rider rider, IEnumerable<TrackStatisticsItem> StatisticsItem, DateTimeOffset FromTime, string OrderBy, int Count)
        {
            var query =
                from tsi in _context.Set<TransponderStatisticsItem>()
                where
                    StatisticsItem.Contains(tsi.StatisticsItem)
                    && tsi.EndTime < FromTime
                    && tsi.Time >= tsi.StatisticsItem.MinTime
                    && tsi.Time <= tsi.StatisticsItem.MaxTime
                    && tsi.Rider == rider
                select tsi;

            query = query.ApplySort(OrderBy).Take(Count);
            query = query.Include(x => x.Rider);
            query = query.Include(x => x.Transponder);
            query = query.Include(x => x.StatisticsItem).ThenInclude(x => x.Layout);

            return await query
                .AsNoTracking()
                .ToListAsync();
        }

        private async Task<Dictionary<long, IEnumerable<Intermediate>>> GetIntermediates(IEnumerable<TransponderStatisticsItem> Passings)
        {
            var Inters = new Dictionary<long, IEnumerable<Intermediate>>();

            foreach (var passing in Passings)
            {
                var query =
                    from inters in _context.Set<TrackSectorPassing>()
                    join sector in _context.Set<TrackLayoutSector>() on inters.TrackSector equals sector.Sector
                    where
                    inters.Transponder == passing.Transponder
                    && inters.StartTime >= passing.StartTime
                    && inters.EndTime <= passing.EndTime
                    && sector.Layout == passing.StatisticsItem.Layout
                    select inters;
                _logger.LogDebug(query.ToQueryString());
                var result = await query.AsNoTracking().ToListAsync();

                Inters.Add(passing.Id, result.Select(x => new Intermediate { Speed = x.Speed * 3.6, Time = x.Time }));
            }

            return Inters;
        }
    }
}
