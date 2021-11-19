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
    public class SegmentsController : GenericController<Segment>
    {
        private readonly ISegmentService _segmentService;
        
        public SegmentsController(ISegmentService segmentService,
                                  ILogger<GenericController<Segment>> logger,
                                  VeloTimerDbContext context) : base(logger, context)
        {
            _segmentService = segmentService;
        }

        [AllowAnonymous]
        public override async Task<ActionResult<Segment>> Get(long id)
        {
            var value = await _dbset
                .AsNoTracking()
                .Include(s => s.Intermediates)
                .Where(s => s.Id == id)
                .SingleOrDefaultAsync();

            if (value == null)
            {
                return NotFound();
            }

            return value;
        }

        [HttpGet("times")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<SegmentTimeRider>>> GetTimes(long segmentId, DateTime fromtime, DateTimeOffset? totime, int? count)
        {
            if (count.HasValue && count.Value < 0)
            {
                ModelState.AddModelError(nameof(count), "Please request a positive number of elemets.");
            }
            if (totime.HasValue && totime < fromtime)
            {
                ModelState.AddModelError(nameof(totime), "{totime.Value} is before {fromtime}.");
            }
            if (ModelState.ErrorCount > 0)
            {
                return BadRequest(ModelState);
            }

            var segmenttimes = await _segmentService.GetSegmentTimes(segmentId, fromtime, totime, Count: count.Value);

            return Ok(segmenttimes);
        }

        [AllowAnonymous]
        [HttpGet("fastest")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<SegmentTimeRider>>> GetFastest(long segmentId, DateTime fromtime, DateTimeOffset? totime, int? count)
        {
            if (count.HasValue && count.Value < 0)
            {
                ModelState.AddModelError(nameof(count), "Please request a positive number of elemets.");
            }
            if (totime.HasValue && totime < fromtime)
            {
                ModelState.AddModelError(nameof(totime), "{totime.Value} is before {fromtime}.");
            }
            if (ModelState.ErrorCount > 0)
            {
                return BadRequest(ModelState);
            }

            var segmenttimes = await _segmentService.GetFastestSegmentTimes(segmentId, fromtime, totime, Count: count.Value);
            
            return Ok(segmenttimes);
        }

        [AllowAnonymous]
        [HttpGet("passingcount")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<KeyValuePair<string, int>>>> GetPassingCounts(long segmentId, DateTime fromtime, DateTimeOffset? totime, int? count)
        {
            if (count.HasValue && count.Value < 0)
            {
                ModelState.AddModelError(nameof(count), "Please request a positive number of elemets.");
            }
            if (totime.HasValue && totime < fromtime)
            {
                ModelState.AddModelError(nameof(totime), "{totime.Value} is before {fromtime}.");
            }
            if (ModelState.ErrorCount > 0)
            {
                return BadRequest(ModelState);
            }

            var passingcount = await _segmentService.GetSegmentPassingCount(segmentId, fromtime, totime, count.Value);
            
            return Ok(passingcount);
        }
    }
}
