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

        public async Task<IEnumerable<double>> GetFastestForOwner(Rider rider, StatisticsItem statisticsItem, DateTimeOffset fromtime, DateTimeOffset? totime, int Count)
        {
            totime ??= DateTimeOffset.MaxValue;

            var times = Enumerable.Empty<double>();

            var query = from tss in _context.Set<TransponderStatisticsSegment>()
                        join tsp in _context.Set<TrackSegmentPassing>() on tss.SegmentPassing equals tsp
                        join town in _context.Set<TransponderOwnership>() on tsp.Start.Transponder equals town.Transponder
                        where tss.TransponderStatisticsItem.StatisticsItem.StatisticsItem == statisticsItem
                            && town.Owner == rider
                            && town.OwnedFrom <= tsp.Start.Time
                            && town.OwnedUntil >= tsp.Start.Time
                        group tsp by tss.TransponderStatisticsItem.Id into grouping
                        select grouping.Sum(x => x.Time) into time
                        orderby time
                        select time;

            _logger.LogDebug(query.Take(Count).ToQueryString());

            times = await query.Take(Count).ToListAsync();

            return times;
        }
    }
}
