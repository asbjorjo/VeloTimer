using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

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

        public async Task<int> GetActiveCount(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            totime ??= DateTimeOffset.MaxValue;

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
                .Where(p => p.StatisticsItem.StatisticsItem == statisticsItem)
                .Where(p => EF.Property<ICollection<TransponderStatisticsSegment>>(p, "segmentpassinglist").First().SegmentPassing.Start.Transponder == transponder)
                .Select(p => EF.Property<ICollection<TransponderStatisticsSegment>>(p, "segmentpassinglist").Select(p => p.SegmentPassing.Time).Sum())
                .OrderBy(p => p)
                .Take(Count);

            _logger.LogDebug(query.ToQueryString());

            times = await query.ToListAsync();

            return times;
        }

        public async Task<IEnumerable<double>> GetFastestForOwner(Rider rider, StatisticsItem statisticsItem, DateTimeOffset FromTime, DateTimeOffset? ToTime, int Count)
        {
            var fromtime = FromTime.UtcDateTime;
            var totime = ToTime.HasValue ? ToTime.Value.UtcDateTime : DateTime.MaxValue;

            var times = Enumerable.Empty<double>();

            var query = from tsi in _context.Set<TransponderStatisticsItem>()
                        join town in _context.Set<TransponderOwnership>() on tsi.Transponder equals town.Transponder
                        where
                            tsi.StatisticsItem.StatisticsItem == statisticsItem
                            && tsi.StartTime >= fromtime
                            && tsi.EndTime <= totime
                            && town.Owner == rider
                            && town.OwnedFrom <= tsi.StartTime
                            && town.OwnedUntil >= tsi.EndTime
                        orderby tsi.Time
                        select tsi.Time;
                            
            _logger.LogDebug(query.Take(Count).ToQueryString());

            times = await query.Take(Count).ToListAsync();

            return times;
        }
    }
}
