using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Services
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

        public async Task DeleteRider(string userId)
        {
            var user = await _context.Set<Rider>().SingleOrDefaultAsync(r => r.UserId == userId);

            if (user == null)
            {
                throw new ArgumentNullException(nameof(userId));
            }

            _context.Remove(user);

            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<Rider>> GetActive(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var active = await FindActiveRiderIds(fromtime, totime);

            var riders = await _context.Set<Rider>()
                .AsNoTracking()
                .Where(r => active.Keys.Contains(r.Id))
                .ToListAsync();

            return riders;
        }

        public async Task<int> GetActiveCount(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var active = await FindActiveRiderIds(fromtime, totime);

            return active.Keys.Count();
        }

        private async Task<Dictionary<long, DateTimeOffset>> FindActiveRiderIds(DateTimeOffset fromtime, DateTimeOffset? ToTime)
        {
            var totime = DateTimeOffset.MaxValue;
            if (ToTime.HasValue)
            {
                totime = ToTime.Value;
            }

            var query = from t in _context.TranspondersOwnerships
                        from p in _context.Passings
                        where p.Time >= fromtime && p.Time <= totime 
                            && p.TransponderId == t.TransponderId && p.Time >= t.OwnedFrom && p.Time < t.OwnedUntil
                        orderby p.Time descending
                        group p by t.OwnerId into passings
                        select new { passings.Key, Last = passings.Max(p => p.Time) };

            var riders = await query
                .AsNoTracking()
                .ToDictionaryAsync(k => k.Key, v => v.Last );

            return riders;
        }
    }
}
