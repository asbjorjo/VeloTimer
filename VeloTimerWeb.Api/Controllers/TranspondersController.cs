using Microsoft.AspNetCore.Authorization;
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
    public class TranspondersController : GenericController<Transponder>
    {
        private readonly ITransponderService _service;
        private readonly ISegmentService _segmentService;

        public TranspondersController(ITransponderService service, ISegmentService segmentService, ILogger<GenericController<Transponder>> logger, VeloTimerDbContext context) : base(logger, context)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _segmentService = segmentService ?? throw new ArgumentNullException(nameof(segmentService));
        }

        [AllowAnonymous]
        [Route("activecount")]
        [HttpGet]
        public async Task<ActionResult<int>> GetActiveCount(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            if (fromtime > totime)
            {
                ModelState.AddModelError(nameof(totime), $"{totime} greater than {fromtime}");
                return BadRequest(ModelState);
            }

            return await _service.GetActiveCount(fromtime, totime);
        }

        [Route("active")]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Transponder>>> GetActive(DateTimeOffset FromTime, DateTimeOffset? ToTime)
        {
            var fromtime = FromTime;
            var totime = DateTimeOffset.MaxValue;

            if (ToTime.HasValue)
            {
                totime = ToTime.Value;
            }

            var value = _context.Set<Passing>().Where(p => p.Time >= fromtime && p.Time <= totime).Select(p => p.Transponder).Distinct();
            
            return await value.ToListAsync();
        }

        [AllowAnonymous]
        [Route("times")]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SegmentTimeRider>>> GetTimes(long SegmentId, [FromQuery] IEnumerable<long> TransponderId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int? Count)
        {
            var times = Enumerable.Empty<SegmentTimeRider>();

            foreach (var transponderId in TransponderId)
            {
                var onetimes = await _segmentService.GetSegmentTimes(SegmentId, FromTime, ToTime);
                times.Concat(onetimes);
            }

            return Ok(times.OrderByDescending(t => t.PassingTime));
        }
    }
}
