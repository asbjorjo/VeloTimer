using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Data.Models.Timing;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.Timing;
using VeloTimerWeb.Api.Models.TrackSetup;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    public class PassingsController : BaseController
    {
        private readonly IPassingService _passingService;
        private readonly VeloTimerDbContext _context;
        private readonly DbSet<Passing> _dbset;

        public PassingsController(
            IMapper mapper,
            IPassingService passingService,
            VeloTimerDbContext context,
            ILogger<PassingsController> logger) : base(mapper, logger)
        {
            _passingService = passingService;
            _context = context;
            _dbset = _context.Set<Passing>();
        }

        [AllowAnonymous]
        [Route("mostrecent")]
        [Route("mostrecent/{Track}")]
        [HttpGet]
        public async Task<ActionResult<PassingWeb>> GetMostRecent(string Track = "")
        {
            var dbset = _dbset
                    .Include(x => x.Loop)
                    .Include(x => x.Transponder)
                    .AsNoTracking();

            if (!string.IsNullOrEmpty(Track))
            {
                dbset = dbset.Where(x => x.Loop.Track.Slug == Track);
            }

            var value = await dbset
                    .OrderByDescending(x => x.Time)
                    .FirstOrDefaultAsync();

            if (value == null)
            {
                return NotFound();
            }

            return _mapper.Map<PassingWeb>(value);
        }
    }
}
