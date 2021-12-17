using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StatisticsItemController : ControllerBase
    {
        private readonly VeloTimerDbContext _context;
        private readonly ILogger<StatisticsItemController> _logger;

        public StatisticsItemController(VeloTimerDbContext context, ILogger<StatisticsItemController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        [Route("{Label}/track/{Track}")]
        public async Task<ActionResult<IEnumerable<TrackStatisticsItem>>> GetForTrack(string Label, string Track)
        {
            var track = await _context.Set<Track>().FindAsync(long.Parse(Track));

            if (track == null)
            {
                return NotFound();
            }

            var items = await _context.Set<TrackStatisticsItem>()
                .Include(x => x.StatisticsItem)
                .Include(x => x.Layout)
                .Where(x => x.Layout.Track == track)
                .Where(x => x.StatisticsItem.Label == Label)
                .ToListAsync();

            return items.Count > 0 ? items : NotFound();
        }

        [HttpGet]
        [Route("track/{Track}")]
        public async Task<ActionResult<IEnumerable<TrackStatisticsItem>>> GetForTrack(string Track)
        {
            var track = await _context.Set<Track>().FindAsync(long.Parse(Track));

            if (track == null)
            {
                return NotFound();
            }

            var items = await _context.Set<TrackStatisticsItem>()
                .Include(x => x.StatisticsItem)
                .Include(x => x.Layout)
                .Where(x => x.Layout.Track == track)
                .ToListAsync();

            return items;
        }
    }
}
