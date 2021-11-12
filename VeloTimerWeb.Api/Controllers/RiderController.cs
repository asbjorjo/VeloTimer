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
    [Route("[controller]")]
    [ApiController]
    public class RiderController : GenericController<Rider>
    {
        private readonly IRiderService _riderService;

        public RiderController(IRiderService riderService, ILogger<GenericController<Rider>> logger, VeloTimerDbContext context) : base(logger, context)
        {
            _riderService = riderService;
        }

        [Route("user/{userId}")]
        public async Task<ActionResult<Rider>> Get(string userId)
        {
            var rider = await _context.Set<Rider>().SingleOrDefaultAsync(r => r.UserId == userId);

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
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<IEnumerable<TransponderOwnershipWeb>>> GetTransponders(string rider)
        {
            var transponders = await _context.Set<TransponderOwnership>().Where(to => to.Owner.UserId == rider)
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

            var existing = _dbset.Where(r => r.Transponders.Where(
                t => t.Transponder.SystemId == transponderId
                    && t.Transponder.TimingSystem == TransponderType.TimingSystem.Mylaps_X2
                    && (ownerWeb.OwnedFrom < t.OwnedUntil && t.OwnedFrom < ownerWeb.OwnedUntil)
                ).Any());

            if (existing.Any())
            {
                ModelState.AddModelError(nameof(ownerWeb.TransponderLabel), "Already registered for chosen period.");
                return Conflict(new ValidationProblemDetails(ModelState));
            }

            var dbrider = await _dbset.Where(r => r.UserId == rider).SingleAsync();
            var transponder = await _context.Set<Transponder>().Where(t => t.SystemId == transponderId && t.TimingSystem == TransponderType.TimingSystem.Mylaps_X2).SingleAsync();

            var value = new TransponderOwnership
            {
                OwnedFrom = ownerWeb.OwnedFrom,
                OwnedUntil = ownerWeb.OwnedUntil,
                Owner = dbrider,
                Transponder = transponder
            };

            await _context.AddAsync(value);
            await _context.SaveChangesAsync();
            ownerWeb.Owner = dbrider.Name;

            return CreatedAtAction(nameof(GetTransponders), ownerWeb);
        }
    }
}
