using AutoMapper;
using Microsoft.AspNetCore.Authorization;
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
using VeloTimerWeb.Api.Models;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TranspondersController : BaseController
    {
        private readonly VeloTimerDbContext _context;
        private readonly ITransponderService _service;

        public TranspondersController(IMapper mapper, ITransponderService service, ILogger<TranspondersController> logger, VeloTimerDbContext context) : base(mapper, logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> ListAll([FromQuery] PaginationParameters pagination)
        {
            var transponders = await _service.GetAll(pagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(transponders.Pagination));

            var result = _mapper.Map<IEnumerable<TransponderWeb>>(transponders);

            return Ok(result);
        }

        [Authorize(Roles = "Admin")]
        [Route("ownerships")]
        [HttpGet]
        public async Task<IActionResult> Ownerships([FromQuery] PaginationParameters pagination)
        {
            var transponders = await _service.GetTransponderOwnershipAsync(pagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(transponders.Pagination));

            var result = _mapper.Map<IEnumerable<TransponderOwnershipWeb>>(transponders);

            return Ok(result);
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
        public async Task<ActionResult<IEnumerable<TransponderWeb>>> GetActive(DateTimeOffset FromTime, DateTimeOffset? ToTime)
        {
            var fromtime = FromTime;
            var totime = DateTimeOffset.MaxValue;

            if (ToTime.HasValue)
            {
                totime = ToTime.Value;
            }

            var value = _context.Set<Passing>().Where(p => p.Time >= fromtime && p.Time <= totime).Select(p => p.Transponder).Distinct();

            var transponders = await value.ToListAsync();

            return Ok(_mapper.Map<IEnumerable<TransponderWeb>>(transponders));
        }

        [AllowAnonymous]
        [Route("times")]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<double>>> GetTimes(long SegmentId, [FromQuery] IEnumerable<long> TransponderId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int? Count)
        {
            var times = Enumerable.Empty<double>();

            var fromtime = FromTime ?? DateTimeOffset.MinValue;
            var totime = ToTime ?? DateTimeOffset.MaxValue;

            var Transponder = await _context.Set<Transponder>().SingleOrDefaultAsync(x => x.Id == TransponderId.First());
            if (Transponder == null) { return NotFound(Transponder); }
            var Segment = await _context.Set<StatisticsItem>().SingleOrDefaultAsync(x => x.Id == SegmentId);
            if (Segment == null) { return NotFound(Segment); }

            times = await _service.GetFastest(Transponder, Segment, fromtime, totime);

            return Ok(times);
        }
    }
}
