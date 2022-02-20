using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VeloTime.Storage.Data;
using VeloTime.Storage.Models.Riders;
using VeloTime.Storage.Models.Statistics;
using VeloTime.Storage.Models.Timing;
using VeloTimer.Shared.Data.Models;

namespace VeloTime.Services
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

        public async Task<Activity> CreateOrUpdateActivity(Passing passing)
        {
            //_logger.LogDebug("{Transponder} - {Time} - {Loop} - {Track}",
            //    TransponderIdConverter.IdToCode(passing.Transponder.SystemId),
            //    passing.Time,
            //    passing.Loop.LoopId,
            //    passing.Loop.Track.Slug);

            var query = _context.Set<Activity>()
                .Where(x => x.Track == passing.Loop.Track)
                .Where(x => x.Transponder == passing.Transponder)
                .Where(x => x.Sessions.Max(x => x.End) >= passing.Time.AddHours(-1));

            var activity = await query
                .Include(x => x.Transponder)
                .Include(x => x.Sessions)
                .SingleOrDefaultAsync();

            if (activity == null)
            {
                activity = Activity.Create(passing);
                _context.Add(activity);
            }
            else
            {
                var lastSession = activity.Sessions.OrderByDescending(x => x.End).FirstOrDefault();

                if (lastSession?.End >= passing.Time.AddMinutes(-5))
                {
                    lastSession.UpdateEnd(passing);
                }
                else
                {
                    lastSession = Session.Create(passing, activity);
                    activity.Sessions.Add(lastSession);
                    _context.Add(lastSession);
                }
            }

            await _context.SaveChangesAsync();
            return activity;
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

        public async Task<StatisticsItem?> GetItemBySlug(string slug)
        {
            var item = await _context.Set<StatisticsItem>().SingleOrDefaultAsync(x => x.Slug == slug);

            return item;
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

        public async Task<TrackStatisticsItem?> GetTrackItemBySlugs(string item, string track, string layout)
        {
            var Item = await _context.Set<TrackStatisticsItem>().SingleOrDefaultAsync(x => x.StatisticsItem.Slug == item && x.Layout.Slug == layout && x.Layout.Track.Slug == track);

            return Item;
        }

        public async Task<IEnumerable<TrackStatisticsItem>> GetTrackItemsBySlugs(string track, string item)
        {
            var items = _context.Set<TrackStatisticsItem>()
                .Include(x => x.StatisticsItem)
                .Include(x => x.Layout)
                .ThenInclude(x => x.Track)
                .Where(x => x.Layout.Track.Slug == track);

            if (!string.IsNullOrWhiteSpace(item))
            {
                items = items.Where(x => x.StatisticsItem.Slug == item);
            }

            return await items.ToListAsync();
        }
    }
}
