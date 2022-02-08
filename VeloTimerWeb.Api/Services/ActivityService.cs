using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;
using VeloTime.Storage.Data;
using VeloTime.Storage.Models.Statistics;
using VeloTimer.Shared.Data;
using VeloTimer.Shared.Data.Parameters;

namespace VeloTimerWeb.Api.Services
{
    public class ActivityService : IActivityService
    {
        private readonly VeloTimerDbContext _context;
        private readonly ILogger<ActivityService> _logger;

        public ActivityService(VeloTimerDbContext context, ILogger<ActivityService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<PaginatedList<Activity>> GetActivitesForRider(string RiderId, PaginationParameters pagination)
        {
            var query = _context.Set<Activity>()
                .AsNoTracking()
                .Where(x => x.Transponder.Owners
                    .Where(x => x.Owner.UserId == RiderId)
                    .Where(y => y.OwnedFrom <= x.Sessions.Min(x => x.Start))
                    .Where(y => y.OwnedUntil >= x.Sessions.Max(x => x.End)).Any())
                .OrderByDescending(x => x.Sessions.Max(x => x.End))
                .Include(x => x.Transponder)
                .Include(x => x.Sessions);

            return await query
                .Select(x => new Activity
                {
                    Id = x.Id,
                    Track = x.Track,
                    Transponder = x.Transponder,
                    Sessions = x.Sessions,
                    Rider = x.Transponder.Owners.Where(y => y.Owner.UserId == RiderId).Select(y => y.Owner).First()
                })
                .ToPaginatedListAsync(pagination.PageNumber, pagination.PageSize);
        }

        public async Task<PaginatedList<Activity>> GetActivities(PaginationParameters pagination)
        {
            return await _context.Set<Activity>()
                .AsNoTracking()
                .OrderByDescending(x => x.Sessions.Max(x => x.End))
                .Include(x => x.Transponder)
                .Include(x => x.Sessions)
                .Select(x => new Activity
                {
                    Id = x.Id,
                    Track = x.Track,
                    Transponder = x.Transponder,
                    Sessions = x.Sessions,
                    Rider = x.Transponder.Owners
                        .Where(y => y.OwnedFrom <= x.Sessions.Min(x => x.Start))
                        .Where(y => y.OwnedUntil >= x.Sessions.Max(x => x.End))
                        .Select(y => y.Owner)
                        .SingleOrDefault()
                })
                .ToPaginatedListAsync(pagination.PageNumber, pagination.PageSize);
        }
    }
}
