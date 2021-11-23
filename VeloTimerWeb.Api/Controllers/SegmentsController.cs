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
        public override async Task<ActionResult<IEnumerable<Segment>>> GetAll()
        {
            return await base.GetAll();
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
        public async Task<ActionResult<IEnumerable<SegmentTimeRider>>> GetTimes(long segmentId, DateTime fromtime, DateTimeOffset? totime, int? count, long? riderid)
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

            IEnumerable<SegmentTimeRider> segmenttimes;

            if (riderid.HasValue)
            {
                segmenttimes = await _segmentService.GetSegmentTimesForRider(segmentId, fromtime, totime, riderid.Value, count.Value);
            }
            else
            {
                segmenttimes = await _segmentService.GetSegmentTimes(segmentId, fromtime, totime, Count: count.Value);
            }

            return Ok(segmenttimes);
        }

        [AllowAnonymous]
        [HttpGet("fastest")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<SegmentTimeRider>>> GetFastest(long SegmentId, DateTime FromTime, DateTimeOffset? ToTime, int? Count, long? RiderId, bool? OnePerRider)
        {
            if (Count.HasValue && Count.Value < 0)
            {
                ModelState.AddModelError(nameof(Count), "Please request a positive number of elemets.");
            }
            if (ToTime.HasValue && ToTime < FromTime)
            {
                ModelState.AddModelError(nameof(ToTime), "{ToTime.Value} is before {FromTime}.");
            }
            if (ModelState.ErrorCount > 0)
            {
                return BadRequest(ModelState);
            }

            IEnumerable<SegmentTimeRider> segmenttimes;

            if (RiderId.HasValue)
            {
                segmenttimes = await _segmentService.GetFastestSegmentTimesForRider(SegmentId, FromTime, ToTime, RiderId.Value, Count.Value);
            } else
            {
                segmenttimes = await _segmentService.GetFastestSegmentTimes(SegmentId, FromTime, ToTime, Count: Count.Value, OnePerRider: OnePerRider.Value);
            }
            
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
