using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TrackController : ControllerBase
    {
        private readonly ITrackService _trackService;
        private readonly ILogger<TrackController> _logger;
        private readonly VeloTimerDbContext _context;
        private readonly DbSet<Track> _dbset;

        public TrackController(
            ITrackService trackService,
            ILogger<TrackController> logger, 
            VeloTimerDbContext context) : base()
        {
            _trackService = trackService;
            _logger = logger;
            _context = context;
            _dbset = _context.Set<Track>();
        }

        [AllowAnonymous]
        public async Task<ActionResult<Track>> Get(long id)
        {
            var value = await _dbset.FindAsync(id);

            if (value == null)
            {
                return NotFound();
            }

            return value;
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("fastest/{StatisticsItem}")]
        public async Task<ActionResult<IEnumerable<SegmentTime>>> Fastest(string StatisticsItem)
        {
            var statsitem = await _context.Set<StatisticsItem>().SingleOrDefaultAsync(x => x.Label == StatisticsItem);

            if (statsitem == null)
            {
                return NotFound($"StatisticsItem: {StatisticsItem}");
            }

            var times = await _trackService.GetFastest(statsitem, null, null);

            return Ok(times);
        }
    }
}
