﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Data;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Data.Parameters;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.Riders;
using VeloTimerWeb.Api.Models.Statistics;
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
                join town in _context.Set<TransponderOwnership>() on tsi.Transponder equals town.Transponder
                where
                    tsi.StatisticsItem == statisticsItem
                    && tsi.StartTime >= fromtime
                    && tsi.EndTime <= totime
                    && tsi.Time >= tsi.StatisticsItem.MinTime
                    && tsi.Time <= tsi.StatisticsItem.MaxTime
                    && town.OwnedFrom <= tsi.StartTime
                    && town.OwnedUntil >= tsi.EndTime
                    && town.Owner.IsPublic
                group tsi by new { town.Owner.Id, town.Owner.Name } into g
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
                join town in _context.Set<TransponderOwnership>() on tsi.Transponder equals town.Transponder
                where
                    counter.Contains(tsi.StatisticsItem)
                    && tsi.StartTime >= fromtime
                    && tsi.EndTime <= totime
                    && tsi.Time >= tsi.StatisticsItem.MinTime
                    && tsi.Time <= tsi.StatisticsItem.MaxTime
                    && town.OwnedFrom <= tsi.StartTime
                    && town.OwnedUntil >= tsi.EndTime
                    && town.Owner.IsPublic
                group tsi by new { town.Owner.Id, town.Owner.Name } into g
                orderby g.Count() descending
                select new SegmentDistance
                {
                    Rider = g.Key.Name,
                    Count = g.Count(),
                    Distance = g.Count() * counter.First().Layout.Distance * counter.First().Laps / 1000
                };

            _logger.LogInformation(query.ToQueryString());

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
                join town in _context.Set<TransponderOwnership>() on tsi.Transponder equals town.Transponder
                where
                    StatisticsItems.Contains(tsi.StatisticsItem)
                    && tsi.StartTime >= fromtime
                    && tsi.EndTime <= totime
                    && tsi.Time >= tsi.StatisticsItem.MinTime
                    && tsi.Time <= tsi.StatisticsItem.MaxTime
                    && town.OwnedFrom <= tsi.StartTime
                    && town.OwnedUntil >= tsi.EndTime
                    && town.Owner.IsPublic
                group tsi.Time by new { town.Owner.Id, town.Owner.Name, Distance = tsi.StatisticsItem.Layout.Distance * tsi.StatisticsItem.Laps } into ridertimes
                orderby ridertimes.Min() ascending
                select new SegmentTime
                {
                    Rider = ridertimes.Key.Name,
                    Time = ridertimes.Min(),
                    Speed = ridertimes.Key.Distance / ridertimes.Min() * 3.6
                };

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
                from tsi in _context.Set<TransponderStatisticsItem>()
                join town in _context.Set<TransponderOwnership>() on tsi.Transponder equals town.Transponder
                where
                    tsi.StatisticsItem.Layout.Track == Track
                    && tsi.StatisticsItem.StatisticsItem.Label == Label
                    && tsi.StartTime >= fromtime
                    && tsi.EndTime <= totime
                    && tsi.Time >= tsi.StatisticsItem.MinTime
                    && tsi.Time <= tsi.StatisticsItem.MaxTime
                    && town.OwnedFrom <= tsi.StartTime
                    && town.OwnedUntil >= tsi.EndTime
                    && town.Owner.IsPublic
                group tsi.Time by new { town.Owner.Id, town.Owner.Name, Distance = tsi.StatisticsItem.Layout.Distance * tsi.StatisticsItem.Laps } into ridertimes
                orderby ridertimes.Min() ascending
                select new SegmentTime
                {
                    Rider = ridertimes.Key.Name,
                    Time = ridertimes.Min(),
                    Speed = ridertimes.Key.Distance / ridertimes.Min() * 3.6
                };

            var list = await query
                .Take(Count)
                .AsNoTracking()
                .ToListAsync();

            return list;
        }

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
