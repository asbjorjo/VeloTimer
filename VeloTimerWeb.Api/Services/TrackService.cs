using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

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

        public async Task<IEnumerable<SegmentTime>> GetFastest(TrackStatisticsItem StatisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count)
        {
            var times = Enumerable.Empty<SegmentTime>();
            
            if (FromTime >= ToTime)
                return times;

            var query =
                from tsi in _context.Set<TransponderStatisticsItem>()
                join town in _context.Set<TransponderOwnership>() on tsi.Transponder equals town.Transponder
                where
                    tsi.StatisticsItem == StatisticsItem
                    && tsi.StartTime >= FromTime
                    && tsi.EndTime <= ToTime
                    && town.OwnedFrom <= tsi.StartTime
                    && town.OwnedUntil >= tsi.EndTime
                group tsi.Time by new { town.Owner.Id, town.Owner.Name, tsi.StatisticsItem.StatisticsItem.Distance } into ridertimes
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

            if (FromTime >= ToTime)
                return times;

            var query =
                from tsi in _context.Set<TransponderStatisticsItem>()
                join town in _context.Set<TransponderOwnership>() on tsi.Transponder equals town.Transponder
                where
                    tsi.StatisticsItem == StatisticsItem
                    && tsi.StartTime >= FromTime
                    && tsi.EndTime <= ToTime
                    && town.OwnedFrom <= tsi.StartTime
                    && town.OwnedUntil >= tsi.EndTime
                orderby tsi.EndTime descending
                select new SegmentTime
                {
                    Rider = town.Owner.Name,
                    Time = tsi.Time,
                    Speed = tsi.StatisticsItem.StatisticsItem.Distance / tsi.Time * 3.6,
                    PassingTime = tsi.EndTime
                };

            var list = await query
                .Take(Count)
                .AsNoTracking()
                .ToListAsync();

            return list;
        }
    }
}
