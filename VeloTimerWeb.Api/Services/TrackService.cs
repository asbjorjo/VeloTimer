using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using VeloTimer.Shared.Data;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Data.Parameters;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.Riders;
using VeloTimerWeb.Api.Models.Statistics;
using VeloTimerWeb.Api.Models.Timing;
using VeloTimerWeb.Api.Models.TrackSetup;

namespace VeloTimerWeb.Api.Services
{
    public class TrackService : ITrackService
    {
        private readonly VeloTimerDbContext _context;
        private readonly ILogger<TrackService> _logger;

        public TrackService(VeloTimerDbContext context, ILogger<TrackService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<SegmentDistance>> GetCount(TrackStatisticsItem statisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10)
        {
            var counts = Enumerable.Empty<SegmentDistance>();
            var fromtime = FromTime.UtcDateTime;
            var totime = ToTime.UtcDateTime;

            if (fromtime >= totime)
                return counts;

            await _context.Entry(statisticsItem)
                .Reference(x => x.Layout)
                .LoadAsync();

            var query =
                from tsi in _context.Set<TransponderStatisticsItem>()
                where
                    tsi.StatisticsItem == statisticsItem
                    && tsi.EndTime >= fromtime
                    && tsi.EndTime <= totime
                    && tsi.Time >= tsi.StatisticsItem.MinTime
                    && tsi.Time <= tsi.StatisticsItem.MaxTime
                    && tsi.Rider.IsPublic
                group tsi by new { tsi.Rider.Id, tsi.Rider.Name } into g
                orderby g.Count() descending
                select new SegmentDistance
                {
                    Rider = g.Key.Name,
                    Count = g.Count(),
                    Distance = g.Count() * statisticsItem.Layout.Distance * statisticsItem.Laps / 1000
                };

            counts = await query
                .Take(Count)
                .ToListAsync();

            return counts;
        }

        public async Task<IEnumerable<SegmentDistance>> GetCount(IEnumerable<TrackStatisticsItem> counter, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10)
        {
            if (counter.Count() == 1)
            {
                _logger.LogInformation("Passing a list of one, do it the old way.");

                return await GetCount(counter.First(), FromTime, ToTime, Count);
            }

            var counts = Enumerable.Empty<SegmentDistance>();
            var fromtime = FromTime.UtcDateTime;
            var totime = ToTime.UtcDateTime;

            if (fromtime >= totime)
                return counts;

            foreach (var statisticsItem in counter)
            {
                await _context.Entry(statisticsItem)
                    .Reference(x => x.Layout)
                    .LoadAsync();
            }

            var query =
                from tsi in _context.Set<TransponderStatisticsItem>()
                where
                    counter.Contains(tsi.StatisticsItem)
                    && tsi.EndTime >= fromtime
                    && tsi.EndTime <= totime
                    && tsi.Time >= tsi.StatisticsItem.MinTime
                    && tsi.Time <= tsi.StatisticsItem.MaxTime
                    && tsi.Rider.IsPublic
                group tsi by new { tsi.Rider.Id, tsi.Rider.Name } into g
                orderby g.Count() descending
                select new SegmentDistance
                {
                    Rider = g.Key.Name,
                    Count = g.Count(),
                    Distance = g.Count() * counter.First().Layout.Distance * counter.First().Laps / 1000
                };

            _logger.LogDebug(query.ToQueryString());

            counts = await query
                .Take(Count)
                .ToListAsync();

            return counts;
        }

        public async Task<IEnumerable<SegmentTime>> GetFastest(IEnumerable<TrackStatisticsItem> StatisticsItems, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count)
        {
            var times = Enumerable.Empty<SegmentTime>();
            var fromtime = FromTime.UtcDateTime;
            var totime = ToTime.UtcDateTime;

            if (fromtime >= totime)
                return times;

            var query =
                from tsi in _context.Set<TransponderStatisticsItem>()
                where
                    StatisticsItems.Contains(tsi.StatisticsItem)
                    && tsi.EndTime >= fromtime
                    && tsi.EndTime <= totime
                    && tsi.Time >= tsi.StatisticsItem.MinTime
                    && tsi.Time <= tsi.StatisticsItem.MaxTime
                    && tsi.Rider.IsPublic
                group tsi.Time by new { tsi.Rider.Id, tsi.Rider.Name, Distance = tsi.StatisticsItem.Layout.Distance * tsi.StatisticsItem.Laps } into ridertimes
                orderby ridertimes.Min() ascending
                select new SegmentTime
                {
                    Rider = ridertimes.Key.Name,
                    Time = ridertimes.Min(),
                    Speed = ridertimes.Key.Distance / ridertimes.Min() * 3.6
                };

            _logger.LogDebug(query.ToQueryString());

            var list = await query
                .Take(Count)
                .AsNoTracking()
                .ToListAsync();

            return list;
        }

        public async Task<IEnumerable<SegmentTime>> GetFastest(Track Track, string Label, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10)
        {
            var times = Enumerable.Empty<SegmentTime>();
            var fromtime = FromTime.UtcDateTime;
            var totime = ToTime.UtcDateTime;

            if (fromtime >= totime)
                return times;

            var query =
                from tsi in _context.Set<TrackStatisticsItem>()
                where
                    tsi.StatisticsItem.Label == Label
                    && tsi.Layout.Track == Track
                select tsi;

            var StatisticsItems = await query.AsNoTracking().ToListAsync();

            _logger.LogDebug(query.ToQueryString());

            var list = await GetFastest(StatisticsItems, FromTime, ToTime, Count);

            return list;
        }

        [Obsolete("Use method with one date parameter", true)]
        public async Task<IEnumerable<SegmentTime>> GetRecent(TrackStatisticsItem StatisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 50)
        {
            var times = Enumerable.Empty<SegmentTime>();
            var fromtime = FromTime.UtcDateTime;
            var totime = ToTime.UtcDateTime;

            if (fromtime >= totime)
                return times;

            var query =
                from tsi in _context.Set<TransponderStatisticsItem>()
                join town in _context.Set<TransponderOwnership>() on tsi.Transponder equals town.Transponder
                where
                    tsi.StatisticsItem == StatisticsItem
                    && tsi.StartTime >= fromtime
                    && tsi.EndTime <= totime
                    && tsi.Time >= tsi.StatisticsItem.MinTime
                    && tsi.Time <= tsi.StatisticsItem.MaxTime
                    && town.OwnedFrom <= tsi.StartTime
                    && town.OwnedUntil >= tsi.EndTime
                    && town.Owner.IsPublic
                orderby tsi.EndTime descending
                select new SegmentTime
                {
                    Rider = town.Owner.Name,
                    Time = tsi.Time,
                    Speed = tsi.Speed * 3.6,
                    PassingTime = tsi.EndTime,
                    Intermediates = tsi.LayoutPassingList.SelectMany(x => x.LayoutPassing.Passings).Select(x => new Intermediate { Speed = x.Speed * 3.6, Time = x.Time })
                };

            var list = await query
                .Take(Count)
                .AsNoTracking()
                .ToListAsync();

            return list;
        }

        [Obsolete("Use method with one date parameter", true)]
        public async Task<PaginatedList<SegmentTime>> GetRecent(IEnumerable<TrackStatisticsItem> statisticsItems, TimeParameters time, PaginationParameters pagination, string orderby)
        {
            var fromtime = time.FromTime;
            var totime = time.ToTime;

            if (fromtime >= totime)
                return null;

            var query =
                from tsi in _context.Set<TransponderStatisticsItem>()
                join town in _context.Set<TransponderOwnership>() on tsi.Transponder equals town.Transponder
                where
                    statisticsItems.Contains(tsi.StatisticsItem)
                    && tsi.StartTime >= fromtime
                    && tsi.EndTime <= totime
                    && tsi.Time >= tsi.StatisticsItem.MinTime
                    && tsi.Time <= tsi.StatisticsItem.MaxTime
                    && town.OwnedFrom <= tsi.StartTime
                    && town.OwnedUntil >= tsi.EndTime
                    && town.Owner.IsPublic
                select new SegmentTime
                {
                    Rider = town.Owner.Name,
                    Time = tsi.Time,
                    Speed = tsi.Speed * 3.6,
                    PassingTime = tsi.EndTime,
                    Intermediates = tsi.LayoutPassingList.SelectMany(x => x.LayoutPassing.Passings).Select(x => new Intermediate { Speed = x.Speed * 3.6, Time = x.Time })
                };

            query = query.ApplySort(orderby);

            var times = await query
                .AsNoTracking()
                .ToPaginatedListAsync(pagination.PageNumber, pagination.PageSize);

            return times;
        }

        public async Task<IEnumerable<SegmentTime>> GetRecent(IEnumerable<TrackStatisticsItem> statisticsItems, DateTimeOffset FromTime, string OrderBy, int Count = 50, bool IncludeIntermediates = true)
        {
            var times = Enumerable.Empty<SegmentTime>();

             var passings = await GetPassings(statisticsItems, FromTime, OrderBy, Count);

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

        private async Task<IEnumerable<TransponderStatisticsItem>> GetPassings(IEnumerable<TrackStatisticsItem> StatisticsItem, DateTimeOffset FromTime, string OrderBy, int Count)
        {
            var query =
                from tsi in _context.Set<TransponderStatisticsItem>()
                where
                    StatisticsItem.Contains(tsi.StatisticsItem)
                    && tsi.EndTime < FromTime
                    && tsi.Time >= tsi.StatisticsItem.MinTime
                    && tsi.Time <= tsi.StatisticsItem.MaxTime
                    && tsi.Rider.IsPublic
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
            var Inters = new Dictionary<long,IEnumerable<Intermediate>>();

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
                    orderby inters.EndTime ascending
                    select inters;
                _logger.LogDebug(query.ToQueryString());
                var result = await query.AsNoTracking().ToListAsync();

                Inters.Add(passing.Id, result.Select(x => new Intermediate { Speed = x.Speed * 3.6, Time = x.Time }));
            }

            return Inters;
        }

        public async Task<Track> GetTrackBySlug(string slug)
        {
            var track = await _context.Set<Track>()
                .Include(x => x.TimingLoops)
                .Include(x => x.Layouts)
                .SingleOrDefaultAsync(x => x.Slug == slug);

            return track;
        }
    }
}
