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
            var value = await _dbset.AsNoTracking().Include(s => s.Intermediates).Where(s => s.Id == id).SingleOrDefaultAsync();

            if (value == null)
            {
                return NotFound();
            }

            return value;
        }

        [HttpGet("times")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<SegmentTimeRider>>> GetTimes(long segmentId, long? transponderId, DateTime? fromtime, DateTimeOffset? totime, int? Count)
        {
            DateTimeOffset _fromtime = DateTimeOffset.Now;

            if (fromtime.HasValue)
            {
                _fromtime = fromtime.Value;
            }

            var segmenttimes = await _segmentService.GetSegmentTimesNew(segmentId, _fromtime, totime, Count: Count.Value);

            return segmenttimes.ToList();
        }

        [AllowAnonymous]
        [HttpGet("fastest")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<SegmentTimeRider>>> GetFastest(long segmentId, long? transponderId, DateTime? fromtime, DateTimeOffset? totime, int? count, bool requireintermediates)
        {
            DateTimeOffset _fromtime = DateTimeOffset.Now;

            if (fromtime.HasValue)
            {
                _fromtime = fromtime.Value;
            }

            var segmenttimes = await _segmentService.GetFastestSegmentTimesNewWay(segmentId, _fromtime, totime); 

            return segmenttimes.ToList();
        }

        [AllowAnonymous]
        [HttpGet("passingcount")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<KeyValuePair<string, long>>>> GetPassingCounts(long segmentId, long? transponderId, DateTime? fromtime, DateTimeOffset? totime, int? count)
        {
            DateTimeOffset _fromtime = DateTimeOffset.Now;

            if (fromtime.HasValue)
            {
                _fromtime = fromtime.Value;
            }

            var passingcount = await _segmentService.GetSegmentPassingCount(segmentId, transponderId, _fromtime, totime, count);
            
            return Ok(passingcount);
        }
    }
}
