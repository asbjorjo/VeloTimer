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

        public TranspondersController(ITransponderService service, ILogger<GenericController<Transponder>> logger, VeloTimerDbContext context) : base(logger, context)
        {
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

        [Route("active")]
        [HttpGet]
        public async Task<ActionResult<ICollection<Transponder>>> GetActive(TimeSpan period, DateTimeOffset? fromtime)
        {
            DateTimeOffset _fromtime = DateTimeOffset.Now;

            if (fromtime.HasValue)
            {
                _fromtime = fromtime.Value;
            }
            
            var value = _dbset.Where(t => t.Passings.Where(p => p.Time > _fromtime - period && p.Time <= _fromtime).Any())
                              .OrderByDescending(t => t.Passings.OrderByDescending(p => p.Time).First());
            
            return await value.ToListAsync();
        }
    }
}
