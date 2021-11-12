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

        public async Task<IEnumerable<Rider>> GetActive(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var active = await FindActiveRiderIds(fromtime, totime);

            var riders = await _context.Set<Rider>().Where(r => active.Keys.Contains(r.Id)).ToListAsync();

            return riders;
        }

        public async Task<int> GetActiveCount(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var active = await FindActiveRiderIds(fromtime, totime);

            return active.Keys.Count();
        }

        private async Task<Dictionary<long, DateTimeOffset>> FindActiveRiderIds(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var query = from t in _context.TranspondersOwnerships
                        from p in _context.Passings
                        where p.Time >= fromtime && t.TransponderId == t.TransponderId && p.Time >= t.OwnedFrom && p.Time < t.OwnedUntil
                        orderby p.Time descending
                        group p by t.OwnerId into passings
                        select new { passings.Key, Last = passings.Max(p => p.Time) };

            var riders = await query.ToDictionaryAsync(k => k.Key, v => v.Last );

            return riders;
        }
    }
}
