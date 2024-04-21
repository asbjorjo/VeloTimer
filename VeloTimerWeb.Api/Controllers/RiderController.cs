using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Data.Models.Riders;
using VeloTimer.Shared.Data.Models.Timing;
using VeloTimer.Shared.Data.Parameters;
using VeloTimer.Shared.Util;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.Riders;
using VeloTimerWeb.Api.Models.Statistics;
using VeloTimerWeb.Api.Models.Timing;
using VeloTimerWeb.Api.Models.TrackSetup;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    [Authorize]
    public class RiderController : BaseController
    {
        private readonly IRiderService _riderService;
        private readonly ITransponderService _transponderService;
        private readonly VeloTimerDbContext _context;
        private readonly DbSet<Rider> _dbset;

        public RiderController(IMapper mapper, IRiderService riderService, ITransponderService transponderService, ILogger<RiderController> logger, VeloTimerDbContext context) : base(mapper, logger)
        {
            _riderService = riderService;
            _transponderService = transponderService;
            _context = context;
            _dbset = _context.Set<Rider>();
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] PaginationParameters pagination)
        {
            var riders = await _riderService.GetAll(pagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(riders.Pagination));

            var response = _mapper.Map<IEnumerable<RiderWeb>>(riders);

            return Ok(response);
        }

        [HttpGet]
        [Route("{userId}")]
        public async Task<ActionResult<RiderWeb>> Get(string userId)
        {
            var rider = await _riderService.GetRiderByUserId(userId);

            if (rider == null)
            {
                return NotFound();
            }

            return _mapper.Map<RiderWeb>(rider);
        }

        [HttpDelete]
        [Route("{userId}")]
        public async Task<ActionResult> Delete(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return BadRequest();
            if (User.Identity.IsAuthenticated && User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value == userId)
            {
                await _riderService.DeleteRider(userId);

                return Ok();
            }

            return Unauthorized();
        }

        [HttpPut]
        [Route("{userid}")]
        public async Task<ActionResult<RiderWeb>> Update(string userId, RiderWeb profile)
        {
            if (string.IsNullOrWhiteSpace(userId)) return BadRequest();

            if (userId != profile.UserId) return BadRequest();

            if (User.Identity.IsAuthenticated && User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value == userId)
            {
                _logger.LogInformation(User?.Identity.Name);
                foreach (var claim in User.Claims)
                {
                    _logger.LogInformation($"Claim: {claim.Type} - {claim.Value} - {claim.Subject}");
                }

                var rider = _mapper.Map<Rider>(profile);
                var success = await _riderService.UpdateRider(rider);

                if (success)
                    return _mapper.Map<RiderWeb>(rider);

                return BadRequest();
            }

            return Unauthorized();
        }

        [Route("active")]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<RiderWeb>>> GetActive(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var active = await _riderService.GetActive(fromtime, totime);

            return Ok(_mapper.Map<IEnumerable<RiderWeb>>(active));
        }

        [AllowAnonymous]
        [Route("activecount")]
        [HttpGet]
        public async Task<ActionResult<int>> GetActiveCount(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var active = await _riderService.GetActiveCount(fromtime, totime);

            return active;
        }

        [HttpGet]
        [Route("{rider}/transponders")]
        public async Task<ActionResult<IEnumerable<TransponderOwnershipWeb>>> GetTransponders(string rider)
        {
            var transponders1 = await _context.Set<TransponderOwnership>()
                .AsNoTracking()
                .Where(to => to.Owner.UserId == rider)
                .OrderByDescending(to => to.OwnedUntil)
                .Include(x => x.Owner)
                .Include(x => x.Transponder)
                .ToListAsync();

            var transponders = _mapper.Map<IEnumerable<TransponderOwnershipWeb>>(transponders1);

            return Ok(transponders);
        }

        [HttpGet]
        [Route("{rider}/fastest/{statsitem}")]
        public async Task<ActionResult<IEnumerable<SegmentTime>>> GetFastest(
            string rider,
            string statsitem,
            DateTimeOffset? FromTime,
            DateTimeOffset? ToTime)
        {
            var fromtime = FromTime ?? DateTimeOffset.MinValue;
            var totime = ToTime ?? DateTimeOffset.MaxValue;

            var Rider = await _context.Set<Rider>().SingleOrDefaultAsync(x => x.UserId == rider);
            if (Rider == null) { return NotFound($"Rider: {rider}"); }
            var StatsItem = await _context.Set<StatisticsItem>().SingleOrDefaultAsync(x => x.Slug == statsitem);
            if (StatsItem == null) { return NotFound($"StatsItem: {statsitem}"); }

            var times = await _transponderService.GetFastestForOwner(Rider, StatsItem, fromtime, totime);
            return times.ToList();
        }

        [HttpGet]
        [Route("{rider}/times/{statsitem}")]
        public async Task<IActionResult> GetTimes(
            string rider,
            string statsitem,
            DateTimeOffset? FromTime,
            int Count = 10,
            string orderBy = "passingtime:desc",
            bool IncludeIntermediate = true)
        {
            var Rider = await _context.Set<Rider>().SingleOrDefaultAsync(x => x.UserId == rider);
            if (Rider == null) { return NotFound($"Rider: {rider}"); }
            var StatsItem = await _context.Set<StatisticsItem>().SingleOrDefaultAsync(x => x.Slug == statsitem);
            if (StatsItem == null) { return NotFound($"StatsItem: {statsitem}"); }

            var fromtime = DateTimeOffset.MaxValue;
            if (FromTime.HasValue) fromtime = FromTime.Value;

            var times = await _transponderService.GetTimesForOwner(Rider, StatsItem, fromtime, orderBy, Count, IncludeIntermediate);

            return Ok(times);
        }

        [HttpGet]
        [Route("{rider}/times/{statsitem}/{track}")]
        public async Task<IActionResult> GetTimes(
            string rider,
            string statsitem,
            string track,
            DateTimeOffset? FromTime,
            int Count = 10,
            string orderBy = "passingtime:desc",
            bool IncludeIntermediate = true)
        {
            var Rider = await _context.Set<Rider>().SingleOrDefaultAsync(x => x.UserId == rider);
            if (Rider == null) { return NotFound($"Rider: {rider}"); }
            var Track = await _context.Set<Track>().SingleOrDefaultAsync(x => x.Slug == track);
            if (Track == null) { return NotFound($"Track: {track}"); }
            var StatsItems = await _context.Set<TrackStatisticsItem>().Where(x => x.StatisticsItem.Slug == statsitem).Where(x => x.Layout.Track == Track).ToListAsync();
            if (!StatsItems.Any()) { return NotFound($"StatsItem: {statsitem}"); }

            var fromtime = DateTimeOffset.MaxValue;
            if (FromTime.HasValue) fromtime = FromTime.Value;

            var times = await _transponderService.GetTimesForOwner(Rider, StatsItems, fromtime, orderBy, Count, IncludeIntermediate);

            return Ok(times);
        }

        [HttpGet]
        [Route("{rider}/times/{statsitem}/{track}/{layout}")]
        public async Task<IActionResult> GetTimes(
            string rider,
            string statsitem,
            string track,
            string layout,
            DateTimeOffset? FromTime,
            int Count = 10,
            string orderBy = "passingtime:desc",
            bool IncludeIntermediate = true)
        {
            var Rider = await _context.Set<Rider>().SingleOrDefaultAsync(x => x.UserId == rider);
            if (Rider == null) { return NotFound($"Rider: {rider}"); }
            var Layout = await _context.Set<TrackLayout>().SingleOrDefaultAsync(x => x.Slug == layout && x.Track.Slug == track);
            if (Layout == null) { return NotFound($"Layout: {layout}"); }
            var StatsItem = await _context.Set<TrackStatisticsItem>().SingleOrDefaultAsync(x => x.Layout == Layout && x.StatisticsItem.Slug == statsitem);
            if (StatsItem == null) { return NotFound($"StatsItem: {statsitem}"); }

            var fromtime = DateTimeOffset.MaxValue;
            if (FromTime.HasValue) fromtime = FromTime.Value;

            var times = await _transponderService.GetTimesForOwner(Rider, StatsItem, fromtime, orderBy, Count, IncludeIntermediate);

            return Ok(times);
        }

        [HttpPost]
        [Route("{rider}/transponders")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<TransponderOwnershipWeb>> RegisterTransponder(string rider, TransponderOwnershipWeb ownerWeb)
        {
            _logger.LogInformation($"Transponder: {ownerWeb.Transponder.Label}"
                                   + $" - Name: {ownerWeb.Owner}"
                                   + $" - Validity: {ownerWeb.OwnedFrom}-{ownerWeb.OwnedUntil}");

            var transponderId = TransponderIdConverter.CodeToId(ownerWeb.Transponder.Label).ToString();

            var transponder = await _context.Set<Transponder>().SingleOrDefaultAsync(t => t.SystemId == transponderId && t.TimingSystem == TransponderType.TimingSystem.Mylaps_X2);
            var ownfrom = ownerWeb.OwnedFrom.ToUniversalTime();
            var ownuntil = ownerWeb.OwnedUntil.ToUniversalTime();

            if (transponder == null)
            {
                transponder = new Transponder { SystemId = transponderId, TimingSystem = TransponderType.TimingSystem.Mylaps_X2 };
                _context.Add(transponder);
            }
            else
            {
                var existing = _dbset.Where(r => r.Transponders.Where(
                    t => t.Transponder.SystemId == transponderId
                        && t.Transponder.TimingSystem == TransponderType.TimingSystem.Mylaps_X2
                        && (ownfrom <= t.OwnedUntil && t.OwnedFrom <= ownuntil)
                    ).Any());

                if (existing.Any())
                {
                    ModelState.AddModelError(nameof(ownerWeb.Transponder.Label), "Already registered for chosen period.");
                    return Conflict(new ValidationProblemDetails(ModelState));
                }
            }

            var dbrider = await _dbset.Where(r => r.UserId == rider).SingleAsync();

            var value = new TransponderOwnership
            {
                OwnedFrom = ownfrom.UtcDateTime,
                OwnedUntil = ownuntil.UtcDateTime,
                Owner = dbrider,
                Transponder = transponder
            };

            await _context.AddAsync(value);
            await _context.SaveChangesAsync();

            return Ok(_mapper.Map<TransponderOwnershipWeb>(value));
        }

        [HttpPut]
        [Route("{rider}/transponder/{label}")]
        public async Task<ActionResult> ExtendTransponderRegistration(string rider, string label, TransponderOwnershipWeb transponderOwnership)
        {
            if (string.IsNullOrWhiteSpace(rider)) return BadRequest();
            if (string.IsNullOrWhiteSpace(label)) return BadRequest();

            if ((User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value == rider) || User.IsInRole("Admin"))
            {
                var ownerships = await _context.Set<TransponderOwnership>()
                    .Where(x => x.Owner.UserId == rider)
                    .Where(x => x.Transponder.SystemId == label)
                    .Where(x => x.OwnedFrom == transponderOwnership.OwnedFrom.UtcDateTime)
                    .Where(x => x.OwnedUntil <= transponderOwnership.OwnedUntil.UtcDateTime)
                    .ToListAsync();

                if (!ownerships.Any()) return NotFound();

                var latest = ownerships.Last();
                latest.OwnedUntil = transponderOwnership.OwnedUntil.UtcDateTime;
                await _context.SaveChangesAsync();

                return Ok();
            }

            return Unauthorized();
        }

        [HttpDelete]
        [Route("{rider}/transponder/{label}/{from}/{until}")]
        public async Task<ActionResult> RemoveTransponderOwnership(string rider, string label, DateTimeOffset from, DateTimeOffset until)
        {
            if (string.IsNullOrWhiteSpace(rider)) return BadRequest();
            if (string.IsNullOrWhiteSpace(label)) return BadRequest();

            if ((User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value == rider) || User.IsInRole("Admin"))
            {
                var ownerships = await _context.Set<TransponderOwnership>()
                    .Where(x => x.Owner.UserId == rider)
                    .Where(x => x.Transponder.SystemId == label)
                    .Where(x => x.OwnedFrom == from.UtcDateTime)
                    .Where(x => x.OwnedUntil == until.UtcDateTime)
                    .ToListAsync();

                if (!ownerships.Any()) return NotFound();

                _context.RemoveRange(ownerships);
                await _context.SaveChangesAsync();

                return Ok();
            }

            return Unauthorized();
        }
    }
}
