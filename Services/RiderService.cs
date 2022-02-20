using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VeloTime.Storage.Data;
using VeloTime.Storage.Models.Riders;
using VeloTime.Storage.Models.Timing;
using VeloTimer.Shared.Data;
using VeloTimer.Shared.Data.Parameters;

namespace VeloTime.Services
{
    public class RiderService : IRiderService
    {
        private readonly VeloTimerDbContext _context;
        private readonly ILogger<RiderService> _logger;

        public RiderService(VeloTimerDbContext context, ILogger<RiderService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<Rider> GetRiderByUserId(string userId)
        {
            var rider = await _context.Set<Rider>().SingleOrDefaultAsync(r => r.UserId == userId);

            return rider;
        }

        public async Task<PaginatedList<Rider>> GetAll(PaginationParameters pagination)
        {
            var query = _context.Set<Rider>();

            var riders = await query.AsNoTracking().ToPaginatedListAsync(pagination.PageNumber, pagination.PageSize);

            return riders;
        }

        public async Task<bool> UpdateRider(Rider rider)
        {
            var dbRider = await _context.Set<Rider>().SingleOrDefaultAsync(x => x.UserId == rider.UserId);

            if (dbRider != null)
            {
                dbRider.IsPublic = rider.IsPublic;
                dbRider.FirstName = rider.FirstName;
                dbRider.LastName = rider.LastName;
                dbRider.Name = rider.Name;

                await _context.SaveChangesAsync();

                return true;
            }

            return false;
        }

        public async Task DeleteRider(string userId)
        {
            var rider = await _context.Set<Rider>().Include(x => x.Transponders).SingleOrDefaultAsync(r => r.UserId == userId);

            if (rider == null)
            {
                throw new ArgumentNullException(nameof(userId));
            }

            _context.Remove(rider);

            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<Rider>> GetActive(DateTimeOffset FromTime, DateTimeOffset? ToTime)
        {
            var fromtime = FromTime.UtcDateTime;
            var totime = ToTime.HasValue ? ToTime.Value.UtcDateTime : DateTimeOffset.MaxValue.UtcDateTime;

            var active = await FindActiveRiderIds(fromtime, totime);

            var riders = await _context.Set<Rider>()
                .AsNoTracking()
                .Where(r => active.ContainsKey(r.Id))
                .Where(r => r.IsPublic)
                .ToListAsync();

            return riders;
        }

        public async Task<int> GetActiveCount(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var active = await FindActiveRiderIds(fromtime, totime);

            return active.Keys.Count;
        }

        private async Task<Dictionary<long, DateTime>> FindActiveRiderIds(DateTimeOffset FromTime, DateTimeOffset? ToTime)
        {
            var fromtime = FromTime.UtcDateTime;
            var totime = ToTime.HasValue ? ToTime.Value.UtcDateTime : DateTimeOffset.MaxValue.UtcDateTime;

            var query = from t in _context.Set<TransponderOwnership>()
                        from p in _context.Set<Passing>()
                        where p.Time >= fromtime && p.Time <= totime
                            && p.Transponder == t.Transponder && p.Time >= t.OwnedFrom && p.Time < t.OwnedUntil
                        orderby p.Time descending
                        group p by t.Owner.Id into passings
                        select new { passings.Key, Last = passings.Max(p => p.Time) };

            var riders = await query
                .AsNoTracking()
                .ToDictionaryAsync(k => k.Key, v => v.Last);

            return riders;
        }
    }
}
