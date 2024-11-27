using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
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
        [Route("{rider}/transponders/{ownershipId}")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<TransponderOwnershipWeb>> RegisterTransponder(string rider, TransponderOwnershipWeb ownerWeb, long? ownershipId)
        {
            _logger.LogInformation($"Transponder: {ownerWeb.Transponder.Label}"
                                   + $" - Name: {ownerWeb.Owner}"
                                   + $" - Validity: {ownerWeb.OwnedFrom}-{ownerWeb.OwnedUntil}");

            var transponderId = TransponderIdConverter.CodeToId(ownerWeb.Transponder.Label).ToString();

            var transponder = await _context.Set<Transponder>().SingleOrDefaultAsync(t => t.SystemId == transponderId && t.TimingSystem == TransponderType.TimingSystem.Mylaps_X2);
            var ownfrom = ownerWeb.OwnedFrom.ToUniversalTime();
            var ownuntil = ownerWeb.OwnedUntil?.ToUniversalTime();

            TransponderOwnership value = default;
            Rider dbrider = await _dbset.Where(r => r.UserId == rider).SingleAsync();

            if (transponder == null)
            {
                transponder = new Transponder { SystemId = transponderId, TimingSystem = TransponderType.TimingSystem.Mylaps_X2 };
                _context.Add(transponder);
            }

            var ownerships = _context.Set<TransponderOwnership>()
                .Where(town => town.Transponder == transponder)
                .Where(town => !town.OwnedUntil.HasValue || ownfrom <= town.OwnedUntil);
            if (ownuntil.HasValue)
            {
                ownerships = ownerships.Where(town => town.OwnedFrom <= ownuntil.Value);
            }

            var differentowner = ownerships.Where(town => town.Owner != dbrider);

            if (differentowner.Any())
            {
                ModelState.AddModelError(nameof(ownerWeb.Transponder.Label), "Registered to different rider.");
                return Conflict(new ValidationProblemDetails(ModelState));
            }

            if (ownershipId.HasValue) {
                value = await _context.Set<TransponderOwnership>()
                    .Where(town => town.Owner == dbrider)
                    .Where(town => town.Id ==  ownershipId)
                    .SingleOrDefaultAsync();
            }

            var selfowner = ownerships.Where(town => town.Owner == dbrider && !town.OwnedUntil.HasValue || town.OwnedUntil >= ownfrom);

            if (value == default && selfowner.IsNullOrEmpty())
            {
                value = new TransponderOwnership
                {
                    OwnedFrom = ownfrom.UtcDateTime,
                    OwnedUntil = ownuntil?.UtcDateTime,
                    Owner = dbrider,
                    Transponder = transponder
                };
                await _context.AddAsync(value);
            }
            else
            {
                value = await selfowner.OrderByDescending(town => town.OwnedFrom).FirstAsync();
                value.OwnedFrom = ownfrom.UtcDateTime;
                value.OwnedUntil = ownuntil?.UtcDateTime;
            }

            DateTimeOffset statsEnd = ownuntil ?? DateTimeOffset.MaxValue.ToUniversalTime();

            var stats = await _context.Set<TransponderStatisticsItem>().Where(x => x.Transponder == transponder && x.StartTime <= ownfrom && x.EndTime <= statsEnd).ToListAsync();

            foreach (var stat in stats)
            {
                stat.Rider = dbrider;
            }

            await _context.SaveChangesAsync();

            return Ok(_mapper.Map<TransponderOwnershipWeb>(value));
        }

        [HttpDelete]
        [Route("{rider}/transponder/{label}")]
        [Route("{rider}/transponder/{label}/{Id:long}")]
        public async Task<ActionResult> RemoveTransponderOwnership(string rider, string label, long? Id)
        {
            if (string.IsNullOrWhiteSpace(rider)) return BadRequest();
            if (string.IsNullOrWhiteSpace(label)) return BadRequest();

            if ((User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value == rider) || User.IsInRole("Admin"))
            {
                IQueryable<TransponderOwnership> ownerships = _context.Set<TransponderOwnership>()
                    .Where(x => x.Owner.UserId == rider)
                    .Where(x => x.Transponder.SystemId == label);

                if (Id.HasValue)
                {
                    ownerships = ownerships.Where(x => x.Id == Id);
                }

                var ownershipsCollection = await ownerships.ToListAsync();

                if (!ownershipsCollection.Any()) return NotFound();

                _context.RemoveRange(ownershipsCollection);
                
                await _context.SaveChangesAsync();

                return Ok();
            }

            return Unauthorized();
        }
    }
}
