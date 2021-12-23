using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StatisticsItemController : ControllerBase
    {
        private readonly VeloTimerDbContext _context;
        private readonly IStatisticsService _statisticsService;
        private readonly ILogger<StatisticsItemController> _logger;

        public StatisticsItemController(IStatisticsService statisticsService, ILogger<StatisticsItemController> logger)
        {
            _statisticsService = statisticsService;
            _logger = logger;
        }

        [HttpGet]
        [Route("{Label}/track/{Track}")]
        public async Task<ActionResult<IEnumerable<TrackStatisticsItemWeb>>> GetForTrack(string Label, string Track)
        {
            var items = await _statisticsService.GetTrackItemsBySlugs(Track, Label);

            return items.Any() ? items.Select(x => x.ToWeb()).ToList() : NotFound();
        }

        [HttpGet]
        [Route("track/{Track}")]
        public async Task<ActionResult<IEnumerable<TrackStatisticsItemWeb>>> GetForTrack(string Track)
        {
            var items = await _statisticsService.GetTrackItemsBySlugs(Track);

            return items.Any() ? items.Select(x => x.ToWeb()).ToList() : NotFound();
        }
    }
}
