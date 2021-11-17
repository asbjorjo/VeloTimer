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
    [Route("[controller]")]
    public class PassingsController : GenericController<Passing>
    {
        private readonly IPassingService _passingService;

        public PassingsController(
            IPassingService passingService,
            VeloTimerDbContext context,
            ILogger<GenericController<Passing>> logger) : base(logger, context)
        {
            _passingService = passingService;
        }

        [AllowAnonymous]
        [Route("mostrecent")]
        [HttpGet]
        public async Task<ActionResult<Passing>> GetMostRecent()
        {
            var value = await _dbset.AsNoTracking().OrderBy(p => p.Source).LastOrDefaultAsync();
            
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
                Source = passing.Source,
                Time = passing.Time
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
