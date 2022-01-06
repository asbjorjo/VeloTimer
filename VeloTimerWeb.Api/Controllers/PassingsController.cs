using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models;
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
        [HttpGet]
        public async Task<ActionResult<PassingWeb>> GetMostRecent()
        {
            var value = await _dbset.AsNoTracking().OrderBy(p => p.SourceId).LastOrDefaultAsync();

            if (value == null)
            {
                return NotFound();
            }

            return _mapper.Map<PassingWeb>(value);
        }

        [AllowAnonymous]
        [Route("register")]
        [HttpPost]
        public async Task<ActionResult<PassingWeb>> Register(PassingRegister passing)
        {
            var existing = await _context.Set<Passing>().SingleOrDefaultAsync(x =>
                x.SourceId == passing.Source
                && x.Loop.LoopId == passing.LoopId
                && x.Time == passing.Time.UtcDateTime);

            if (existing != null)
            {
                return Conflict(existing);
            }

            var newpassing = new Passing
            {
                SourceId = passing.Source,
                Time = passing.Time.UtcDateTime
            };

            var loop = await _context.Set<TimingLoop>()
                .Where(t => t.LoopId == passing.LoopId)
                .SingleOrDefaultAsync();

            if (loop == null)
            {
                ModelState.AddModelError(nameof(passing.LoopId), "Not found.");
                return BadRequest(ModelState);
            }

            newpassing.Loop = loop;

            await _passingService.RegisterNew(newpassing, TransponderType.TimingSystem.Mylaps_X2, passing.TransponderId);

            return Ok(_mapper.Map<PassingWeb>(newpassing));
        }
    }
}
