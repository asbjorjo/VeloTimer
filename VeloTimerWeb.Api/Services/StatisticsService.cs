﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models;

namespace VeloTimerWeb.Api.Services
{
    public class StatisticsService : IStatisticsService
    {
        private readonly VeloTimerDbContext _context;
        private readonly ILogger<StatisticsService> _logger;

        public StatisticsService(VeloTimerDbContext context, ILogger<StatisticsService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<SegmentTime>> GetBestTimes(StatisticsItem StatisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10)
        {
            var times = Enumerable.Empty<SegmentTime>();
            var fromtime = FromTime.UtcDateTime;
            var totime = ToTime.UtcDateTime;

            if (fromtime >= totime)
                return times;

            var query = from tsi in _context.Set<TransponderStatisticsItem>()
                        join town in _context.Set<TransponderOwnership>() on tsi.Transponder equals town.Transponder
                        where
                            tsi.StatisticsItem.StatisticsItem == StatisticsItem
                            && tsi.StartTime >= fromtime
                            && tsi.EndTime <= totime
                            && tsi.Time >= tsi.StatisticsItem.MinTime
                            && tsi.Time <= tsi.StatisticsItem.MaxTime
                            && town.OwnedFrom <= tsi.StartTime
                            && town.OwnedUntil >= tsi.EndTime
                            && town.Owner.IsPublic
                        group tsi.Time by new { town.Owner.Id, town.Owner.Name, tsi.StatisticsItem.StatisticsItem.Distance } into ridertimes
                        orderby ridertimes.Min() ascending
                        select new SegmentTime
                        {
                            Rider = ridertimes.Key.Name,
                            Time = ridertimes.Min(),
                            Speed = ridertimes.Key.Distance / ridertimes.Min() * 3.6
                        };

            times = await query
                .Take(Count)
                .AsNoTracking()
                .ToListAsync();

            return times;
        }

        public async Task<IEnumerable<KeyValuePair<string, double>>> GetTopDistances(StatisticsItem StatisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10)
        {
            var counts = new Dictionary<string, double>();
            var fromtime = FromTime.UtcDateTime;
            var totime = ToTime.UtcDateTime;

            if (fromtime >= totime)
                return counts;

            var query =
                from tsi in _context.Set<TransponderStatisticsItem>()
                join town in _context.Set<TransponderOwnership>() on tsi.Transponder equals town.Transponder
                where
                    tsi.StatisticsItem.StatisticsItem == StatisticsItem
                    && tsi.StartTime >= fromtime
                    && tsi.EndTime <= totime
                    && tsi.Time >= tsi.StatisticsItem.MinTime
                    && tsi.Time <= tsi.StatisticsItem.MaxTime
                    && town.OwnedFrom <= tsi.StartTime
                    && town.OwnedUntil >= tsi.EndTime
                    && town.Owner.IsPublic
                group tsi by new { town.Owner.Id, town.Owner.Name, Length = tsi.StatisticsItem.Layout.Distance } into g
                orderby g.Key.Length * g.Count() descending
                select new
                {
                    Rider = g.Key.Name,
                    Distance = g.Count() * g.Key.Length
                };

            counts = await query
                .Take(Count)
                .ToDictionaryAsync(k => k.Rider, v => v.Distance);

            return counts;
        }
    }
}
