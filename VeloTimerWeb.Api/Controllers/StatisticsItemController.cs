using AutoMapper;
using Microsoft.AspNetCore.Mvc;
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
        private readonly IMapper _mapper;
        private readonly VeloTimerDbContext _context;
        private readonly IStatisticsService _statisticsService;
        private readonly ILogger<StatisticsItemController> _logger;

        public StatisticsItemController(IMapper mapper, IStatisticsService statisticsService, ILogger<StatisticsItemController> logger)
        {
            _mapper = mapper;
            _statisticsService = statisticsService;
            _logger = logger;
        }

        [HttpGet]
        [Route("{Label}/track/{Track}")]
        public async Task<ActionResult<IEnumerable<TrackStatisticsItemWeb>>> GetForTrack(string Label, string Track)
        {
            var items = await _statisticsService.GetTrackItemsBySlugs(Track, Label);

            return items.Any() ? _mapper.Map<List<TrackStatisticsItemWeb>>(items) : NotFound();
        }

        [HttpGet]
        [Route("track/{Track}")]
        public async Task<ActionResult<IEnumerable<TrackStatisticsItemWeb>>> GetForTrack(string Track)
        {
            var items = await _statisticsService.GetTrackItemsBySlugs(Track);

            return items.Any() ? _mapper.Map<List<TrackStatisticsItemWeb>>(items) : NotFound();
        }
    }
}
