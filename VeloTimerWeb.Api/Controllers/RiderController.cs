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
using VeloTimer.Shared.Util;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
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

        [Route("user/{userId}")]
        public async Task<ActionResult<Rider>> Get(string userId)
        {
            var rider = await _context.Set<Rider>().AsNoTracking().SingleOrDefaultAsync(r => r.UserId == userId);

            if (rider == null)
            {
                return NotFound();
            }

            return rider;
        }

        [Route("active")]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Rider>>> GetActive(DateTimeOffset fromtime, DateTimeOffset? totime)
        {
            var active = await _riderService.GetActive(fromtime, totime);

            return active.ToList();
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
            var transponders = await _context.Set<TransponderOwnership>()
                .AsNoTracking()
                .Where(to => to.Owner.UserId == rider)
                .OrderByDescending(to => to.OwnedUntil)
                .Select(to => new TransponderOwnershipWeb 
                { 
                    OwnedFrom = to.OwnedFrom, 
                    OwnedUntil = to.OwnedUntil, 
                    Owner = to.Owner.Name, 
                    TransponderLabel = TransponderIdConverter.IdToCode(long.Parse(to.Transponder.SystemId)) 
                })
                .ToListAsync();
            return transponders;
        }

        [HttpGet]
        [Route("{rider}/fastest/{statsitem}")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<IEnumerable<TransponderOwnershipWeb>>> GetFastest(
            string rider, 
            string statsitem, 
            DateTimeOffset? FromTime,
            DateTimeOffset? ToTime)
        {
            var fromtime = FromTime.HasValue ? FromTime.Value : DateTimeOffset.MinValue;
            var totime = ToTime.HasValue ? ToTime.Value : DateTimeOffset.MaxValue;

            var Rider = await _context.Set<Rider>().SingleOrDefaultAsync(x => x.UserId == rider);
            if (Rider == null) { return NotFound($"Rider: {rider}"); }
            var StatsItem = await _context.Set<StatisticsItem>().SingleOrDefaultAsync(x => x.Label == statsitem);
            if (StatsItem == null) { return NotFound($"StatsItem: {statsitem}"); }

            var times = await _transponderService.GetFastestForOwner(Rider, StatsItem, fromtime, totime);
            return Ok(times);
        }

        [HttpPost]
        [Route("{rider}/transponders")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<TransponderOwnershipWeb>> RegisterTransponder(string rider, TransponderOwnershipWeb ownerWeb)
        {
            _logger.LogInformation($"Transponder: {ownerWeb.TransponderLabel}"
                                   + $" - Name: {ownerWeb.Owner}"
                                   + $" - Validity: {ownerWeb.OwnedFrom}-{ownerWeb.OwnedUntil}");

            var transponderId = TransponderIdConverter.CodeToId(ownerWeb.TransponderLabel).ToString();

            var transponder = await _context.Set<Transponder>().SingleOrDefaultAsync(t => t.SystemId == transponderId && t.TimingSystem == TransponderType.TimingSystem.Mylaps_X2);

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
                        && (ownerWeb.OwnedFrom <= t.OwnedUntil && t.OwnedFrom <= ownerWeb.OwnedUntil)
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
                OwnedFrom = ownerWeb.OwnedFrom.UtcDateTime,
                OwnedUntil = ownerWeb.OwnedUntil.UtcDateTime,
                Owner = dbrider,
                Transponder = transponder
            };

            await _context.AddAsync(value);
            await _context.SaveChangesAsync();
            ownerWeb.Owner = dbrider.Name;

            return Ok();
        }
    }
}
