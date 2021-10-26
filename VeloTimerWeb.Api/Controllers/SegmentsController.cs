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
        private readonly ISegmentService _segmentTimes;
        
        public SegmentsController(ISegmentService segmentTimes,
                                  ILogger<GenericController<Segment>> logger,
                                  ApplicationDbContext context) : base(logger, context)
        {
            _segmentTimes = segmentTimes;
        }

        public override async Task<ActionResult<Segment>> Get(long id)
        {
            var value = await _dbset.Include(s => s.Intermediates).Where(s => s.Id == id).SingleOrDefaultAsync();

            if (value == null)
            {
                return NotFound();
            }

            return value;
        }

        [HttpGet("times")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<SegmentTimeRider>>> GetTimes(long segmentId, long? transponderId, DateTime? fromtime, TimeSpan? period)
        {
            var segmenttimes = await _segmentTimes.GetSegmentTimesAsync(segmentId, transponderId, fromtime, period);

            return segmenttimes.ToList();
        }

        [HttpGet("passingcount")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<long>> GetPassingCounts(long segmentId, long? transponderId, DateTime? fromtime, TimeSpan? period)
        {
            var passingcount = await _segmentTimes.GetSegmentPassingCountAsync(segmentId, transponderId, fromtime, period);

            return passingcount;
        }
    }
}
