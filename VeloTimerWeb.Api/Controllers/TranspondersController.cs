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
    public class TranspondersController : ControllerBase
    {
        private readonly VeloTimerDbContext _context;
        private readonly ILogger<TranspondersController> _logger;
        private readonly ITransponderService _service;
        
        public TranspondersController(ITransponderService service, ILogger<TranspondersController> logger, VeloTimerDbContext context) : base()
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _service = service ?? throw new ArgumentNullException(nameof(service));
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

        [AllowAnonymous]
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

            //foreach (var transponderId in TransponderId)
            //{
            //    var onetimes = await _segmentService.GetSegmentTimes(SegmentId, FromTime, ToTime);
            //    times = times.Concat(onetimes);
            //}

            return Ok(times.OrderByDescending(t => t.PassingTime));
        }
    }
}
