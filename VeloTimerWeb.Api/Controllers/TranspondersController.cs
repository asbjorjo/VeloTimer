using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    public class TranspondersController : GenericController<Transponder>
    {
        public TranspondersController(ILogger<GenericController<Transponder>> logger, ApplicationDbContext context) : base(logger, context)
        {
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
