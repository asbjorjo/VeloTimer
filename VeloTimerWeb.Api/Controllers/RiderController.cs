using IdentityModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimer.Shared.Util;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class RiderController : ControllerBase
    {
        private readonly IRiderService _riderService;
        private readonly ITransponderService _transponderService;
        private readonly ILogger<RiderController> _logger;
        private readonly VeloTimerDbContext _context;
        private readonly DbSet<Rider> _dbset;

        public RiderController(IRiderService riderService, ITransponderService transponderService, ILogger<RiderController> logger, VeloTimerDbContext context) : base()
        {
            _riderService = riderService;
            _transponderService = transponderService;
            _logger = logger;
            _context = context;
            _dbset = _context.Set<Rider>();
        }

        [HttpGet]
        [Route("{userId}")]
        public async Task<ActionResult<RiderWeb>> Get(string userId)
        {
            var rider = await _context.Set<Rider>().AsNoTracking().SingleOrDefaultAsync(r => r.UserId == userId);

            if (rider == null)
            {
                return NotFound();
            }

            return RiderWeb.Create(rider);
        }

        [HttpDelete]
        [Route("{userId}")]
        public async Task<ActionResult> Delete(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return BadRequest();
            if (!User.Identity.IsAuthenticated || User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value != userId) return Unauthorized();

            await _riderService.DeleteRider(userId);

            return Ok();
        }

        [HttpPut]
        [Route("{userid}")]
        public async Task<ActionResult<RiderWeb>> Update(string userId, RiderWeb profile)
        {
            if (string.IsNullOrWhiteSpace(userId)) return BadRequest();
            if (userId != profile.UserId) return BadRequest();
            if (!User.Identity.IsAuthenticated || User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value != userId) return Unauthorized();

            _logger.LogInformation(User?.Identity.Name);
            foreach (var claim in User.Claims)
            {
                _logger.LogInformation($"Claim: {claim.Type} - {claim.Value} - {claim.Subject}");
            }

            var rider = await _context.Set<Rider>().SingleOrDefaultAsync(x => x.UserId == userId);

            if (rider == null) return NotFound();

            rider.Name = profile.RiderDisplayName;
            rider.FirstName = profile.RiderFirstName;
            rider.LastName = profile.RiderLastName;
            rider.IsPublic = profile.RiderIsPublic;

            await _context.SaveChangesAsync();

            return RiderWeb.Create(rider);
        }

        [Route("active")]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<RiderWeb>>> GetActive(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var active = await _riderService.GetActive(fromtime, totime);

            return active.Select(x => RiderWeb.Create(x)).ToList();
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

            var transponders = transponders1
                .Select(to => new TransponderOwnershipWeb
                {
                    OwnedFrom = to.OwnedFrom,
                    OwnedUntil = to.OwnedUntil,
                    Owner = to.Owner.Name,
                    TransponderLabel = TransponderIdConverter.IdToCode(long.Parse(to.Transponder.SystemId))
                }).ToList();

            return transponders;
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
            var StatsItem = await _context.Set<StatisticsItem>().SingleOrDefaultAsync(x => x.Label == statsitem);
            if (StatsItem == null) { return NotFound($"StatsItem: {statsitem}"); }

            var times = await _transponderService.GetFastestForOwner(Rider, StatsItem, fromtime, totime);
            return times.ToList();
        }

        [HttpGet]
        [Route("{rider}/times/{statsitem}")]
        public async Task<ActionResult<IEnumerable<SegmentTime>>> GetTimes(
            string rider,
            string statsitem,
            DateTimeOffset? FromTime,
            DateTimeOffset? ToTime)
        {
            var fromtime = FromTime ?? DateTimeOffset.MinValue;
            var totime = ToTime ?? DateTimeOffset.MaxValue;

            var Rider = await _context.Set<Rider>().SingleOrDefaultAsync(x => x.UserId == rider);
            if (Rider == null) { return NotFound($"Rider: {rider}"); }
            var StatsItem = await _context.Set<StatisticsItem>().SingleOrDefaultAsync(x => x.Label == statsitem);
            if (StatsItem == null) { return NotFound($"StatsItem: {statsitem}"); }

            var times = await _transponderService.GetTimesForOwner(Rider, StatsItem, fromtime, totime);
            return times.ToList();
        }

        [HttpPost]
        [Route("{rider}/transponders")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<TransponderOwnershipWebForm>> RegisterTransponder(string rider, TransponderOwnershipWebForm ownerWeb)
        {
            _logger.LogInformation($"Transponder: {ownerWeb.TransponderLabel}"
                                   + $" - Name: {ownerWeb.Owner}"
                                   + $" - Validity: {ownerWeb.OwnedFrom}-{ownerWeb.OwnedUntil}");

            var transponderId = TransponderIdConverter.CodeToId(ownerWeb.TransponderLabel).ToString();

            var transponder = await _context.Set<Transponder>().SingleOrDefaultAsync(t => t.SystemId == transponderId && t.TimingSystem == TransponderType.TimingSystem.Mylaps_X2);
            var ownfrom = ownerWeb.OwnedFrom.UtcDateTime;
            var ownuntil = ownerWeb.OwnedUntil.UtcDateTime;

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
                    ModelState.AddModelError(nameof(ownerWeb.TransponderLabel), "Already registered for chosen period.");
                    return Conflict(new ValidationProblemDetails(ModelState));
                }
            }

            var dbrider = await _dbset.Where(r => r.UserId == rider).SingleAsync();
            
            var value = new TransponderOwnership
            {
                OwnedFrom = ownfrom,
                OwnedUntil = ownuntil,
                Owner = dbrider,
                Transponder = transponder
            };

            await _context.AddAsync(value);
            await _context.SaveChangesAsync();
            ownerWeb.Owner = dbrider.Name;

            return Ok();
        }

        [HttpDelete]
        [Route("{rider}/transponder/{label}/{from}/{until}")]
        public async Task<ActionResult> RemoveTransponderOwnership(string rider, string label, DateTimeOffset from, DateTimeOffset until)
        {
            if (string.IsNullOrWhiteSpace(rider)) return BadRequest();
            if (string.IsNullOrWhiteSpace(label)) return BadRequest();

            if (User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value != rider) return Unauthorized();

            var ownerships = await _context.Set<TransponderOwnership>()
                .Where(x => x.Owner.UserId == rider)
                .Where(x => x.Transponder.SystemId == TransponderIdConverter.CodeToId(label).ToString())
                .Where(x => x.OwnedFrom == from.UtcDateTime)
                .Where(x => x.OwnedUntil == until.UtcDateTime)
                .ToListAsync();

            if (!ownerships.Any()) return NotFound();

            _context.RemoveRange(ownerships);
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
