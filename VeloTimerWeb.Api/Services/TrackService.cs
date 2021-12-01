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

        public async Task<IEnumerable<SegmentTime>> GetFastest(StatisticsItem StatisticsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count)
        {
            var fromtime = DateTime.MinValue;
            var totime = DateTime.MaxValue;
            var times = Enumerable.Empty<SegmentTime>();

            if (FromTime.HasValue)
                fromtime = FromTime.Value.UtcDateTime;
            if (ToTime.HasValue)
                totime = ToTime.Value.UtcDateTime;

            if (fromtime >= totime)
                return times;

            var query =
                from tsi in _context.Set<TransponderStatisticsItem>()
                join town in _context.Set<TransponderOwnership>() on tsi.Transponder equals town.Transponder
                where
                    tsi.StatisticsItem.StatisticsItem == StatisticsItem
                    && tsi.StartTime >= fromtime
                    && tsi.EndTime <= totime
                    && town.OwnedFrom <= tsi.StartTime
                    && town.OwnedUntil >= tsi.EndTime
                group tsi.Time by new { town.Owner.Id, town.Owner.Name } into ridertimes
                orderby ridertimes.Min() ascending
                select new SegmentTime
                {
                    Rider = ridertimes.Key.Name,
                    Time = ridertimes.Min(),
                    Speed = StatisticsItem.Distance / ridertimes.Min() * 3.6
                };

            var list = await query.Take(Count).ToListAsync();

            return list;
        }
    }
}
