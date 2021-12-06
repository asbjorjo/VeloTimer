using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        [Route("{Track}/count/{StatisticsItem}")]
        public async Task<ActionResult<IEnumerable<KeyValuePair<string, long>>>> Count(string Track, string StatisticsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10)
        {
            if (Track == null || StatisticsItem == null)
            {
                return BadRequest();
            }

            var track = await _context.Set<Track>().FindAsync(long.Parse(Track));
            if (track == null)
            {
                return NotFound($"Track: {Track}");
            }

            var statsitem = await _context.Set<TrackStatisticsItem>().SingleOrDefaultAsync(x => x.Layout.Track == track && x.StatisticsItem.Label == StatisticsItem);
            if (statsitem == null)
            {
                return NotFound($"StatisticsItem: {StatisticsItem}");
            }

            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue) fromtime = FromTime.Value;
            if (ToTime.HasValue) totime = ToTime.Value;

            var counts = await _trackService.GetCount(statsitem, fromtime, totime, Count);

            return Ok(counts);
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("{Track}/fastest/{StatisticsItem}")]
        public async Task<ActionResult<IEnumerable<SegmentTime>>> Fastest(long Track, string StatisticsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10)
        {
            var track = await _context.Set<Track>().FindAsync(Track);
            if (track == null)
            {
                return NotFound($"Track: {Track}"); 
            }

            var statsitem = await _context.Set<TrackStatisticsItem>().SingleOrDefaultAsync(x => x.Layout.Track == track && x.StatisticsItem.Label == StatisticsItem);

            if (statsitem == null)
            {
                return NotFound($"StatisticsItem: {StatisticsItem}");
            }

            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue) fromtime = FromTime.Value;
            if (ToTime.HasValue) totime = ToTime.Value;
            
            var times = await _trackService.GetFastest(statsitem, fromtime, totime, Count);

            return Ok(times);
        }

        [HttpGet]
        [Route("{Track}/times/{StatisticsItem}")]
        public async Task<ActionResult<IEnumerable<SegmentTime>>> Recent(long Track, string StatisticsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 50)
        {
            var track = await _context.Set<Track>().FindAsync(Track);
            if (track == null)
            {
                return NotFound($"Track: {Track}");
            }

            var statsitem = await _context.Set<TrackStatisticsItem>().SingleOrDefaultAsync(x => x.Layout.Track == track && x.StatisticsItem.Label == StatisticsItem);

            if (statsitem == null)
            {
                return NotFound($"StatisticsItem: {StatisticsItem}");
            }

            var fromtime = DateTimeOffset.Now.AddHours(-1);
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue) fromtime = FromTime.Value;
            if (ToTime.HasValue) totime = ToTime.Value;

            var times = await _trackService.GetRecent(statsitem, fromtime, totime, Count);

            return Ok(times);
        }
    }
}
