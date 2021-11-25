using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PassingsController : ControllerBase
    {
        private readonly IPassingService _passingService;
        private readonly ILogger<PassingsController> _logger;
        private readonly VeloTimerDbContext _context;
        private readonly DbSet<Passing> _dbset;

        public PassingsController(
            IPassingService passingService,
            VeloTimerDbContext context,
            ILogger<PassingsController> logger) : base()
        {
            _passingService = passingService;
            _logger = logger;
            _context = context;
            _dbset = _context.Set<Passing>();
        }

        [AllowAnonymous]
        [Route("mostrecent")]
        [HttpGet]
        public async Task<ActionResult<Passing>> GetMostRecent()
        {
            var value = await _dbset.AsNoTracking().OrderBy(p => p.SourceId).LastOrDefaultAsync();
            
            if (value == null)
            {
                return NotFound();
            }

            return value;
        }

        [AllowAnonymous]
        [Route("register")]
        [HttpPost]
        public async Task<ActionResult<Passing>> Register(PassingRegister passing)
        {
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

            return Ok();
        }
    }
}
