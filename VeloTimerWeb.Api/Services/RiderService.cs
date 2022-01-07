using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.Identity;
using VeloTimerWeb.Api.Models.Riders;
using VeloTimerWeb.Api.Models.Timing;

namespace VeloTimerWeb.Api.Services
{
    public class RiderService : IRiderService
    {
        private readonly VeloTimerDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<RiderService> _logger;

        public RiderService(VeloTimerDbContext context, UserManager<User> userManager, ILogger<RiderService> logger)
        {
            _context = context;
            _userManager = userManager;
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
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                var result = await _userManager.DeleteAsync(user);

                if (!result.Succeeded)
                {
                    _logger.LogError(result.ToString());
                }
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
                .Where(r => active.Keys.Contains(r.Id))
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
