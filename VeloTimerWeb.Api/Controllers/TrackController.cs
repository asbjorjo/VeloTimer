using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrackController : ControllerBase
    {
        private readonly ITrackService _trackService;
        private readonly IStatisticsService _statisticsService;
        private readonly ILogger<TrackController> _logger;

        public TrackController(
            ITrackService trackService,
            IStatisticsService statisticsService,
            ILogger<TrackController> logger) : base()
        {
            _trackService = trackService;
            _statisticsService = statisticsService;
            _logger = logger;
        }

        [AllowAnonymous]
        public async Task<ActionResult<TrackWeb>> Get(string slug)
        {
            var value = await _trackService.GetTrackBySlug(slug);

            if (value == null)
            {
                return NotFound();
            }

            return value.ToWeb();
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("{Track}/count/{StatisticsItem}")]
        public async Task<ActionResult<IEnumerable<SegmentDistance>>> Count(string Track, string StatisticsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10)
        {
            if (Track == null || StatisticsItem == null)
            {
                return BadRequest();
            }

            var statsitem = await _statisticsService.GetTrackItemsBySlugs(Track, StatisticsItem);
            if (!statsitem.Any())
            {
                return NotFound($"StatisticsItem: {StatisticsItem}");
            }

            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue) fromtime = FromTime.Value;
            if (ToTime.HasValue) totime = ToTime.Value;

            var counts = await _trackService.GetCount(statsitem.First(), fromtime, totime, Count);

            return Ok(counts);
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("{Track}/fastest/{StatisticsItem}")]
        public async Task<IActionResult> Fastest(string Track, string StatisticsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10)
        {
            var statsitems = await _statisticsService.GetTrackItemsBySlugs(Track, StatisticsItem);

            if (!statsitems.Any())
            {
                return NotFound($"StatisticsItem: {StatisticsItem}");
            }

            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue) fromtime = FromTime.Value;
            if (ToTime.HasValue) totime = ToTime.Value;
            
            var times = await _trackService.GetFastest(statsitems, fromtime, totime, Count);

            return Ok(times);
        }

        [HttpGet]
        [Route("{Track}/times/{StatisticsItem}")]
        public async Task<IActionResult> Recent(
            string Track, 
            string StatisticsItem, 
            [FromQuery] TimeParameters timeParameters,
            [FromQuery] PaginationParameters pagingParameters,
            [FromQuery] string orderBy)
        {
            var statsitems = await _statisticsService.GetTrackItemsBySlugs(Track, StatisticsItem);

            if (!statsitems.Any())
            {
                return NotFound($"StatisticsItem: {StatisticsItem}");
            }

            var times = await _trackService.GetRecent(statsitems, timeParameters, pagingParameters, orderBy);
            
            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(times.Pagination));

            return Ok(times);
        }
    }
}
