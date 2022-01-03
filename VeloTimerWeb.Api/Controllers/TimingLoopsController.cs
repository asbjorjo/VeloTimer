using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    public class TimingLoopsController : ControllerBase
    {
        private readonly ILogger<TimingLoopsController> _logger;
        private readonly VeloTimerDbContext _context;
        private readonly DbSet<TimingLoop> _dbset;

        public TimingLoopsController(VeloTimerDbContext context, ILogger<TimingLoopsController> logger) : base()
        {
            _logger = logger;
            _context = context;
            _dbset = _context.Set<TimingLoop>();
        }

        [AllowAnonymous]
        public async Task<ActionResult<TimingLoop>> Get(long id)
        {
            var value = await _dbset.FindAsync(id);

            if (value == null)
            {
                return NotFound();
            }

            return value;
        }

        [HttpGet]
        [Route("track/{Track}")]
        public async Task<ActionResult<IEnumerable<TimingLoop>>> GetTrack(string Track)
        {
            var track = await _context.Set<Track>().FindAsync(long.Parse(Track));

            if (track == null)
            {
                return NotFound();
            }

            var loops = await _context.Set<TimingLoop>().Where(x => x.Track == track).ToListAsync();

            return loops;
        }
    }
}
