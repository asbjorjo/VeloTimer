using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTime.Services;
using VeloTimer.Shared.Data.Models.Statistics;

namespace VeloTimerWeb.Api.Controllers
{
    public class StatisticsItemController : BaseController
    {
        private readonly IStatisticsService _statisticsService;

        public StatisticsItemController(IMapper mapper, IStatisticsService statisticsService, ILogger<StatisticsItemController> logger) : base(mapper, logger)
        {
            _statisticsService = statisticsService;
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
