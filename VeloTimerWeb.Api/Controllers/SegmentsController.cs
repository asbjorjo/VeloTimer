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
    public class SegmentsController : GenericController<Segment>
    {
        public SegmentsController(ILogger<GenericController<Segment>> logger,
                                  ApplicationDbContext context) : base(logger, context)
        { 
        }

        public override async Task<ActionResult<Segment>> Get(long id)
        {
            var value = await _dbset.Include(s => s.Intermediates).Where(s => s.Id == id).SingleOrDefaultAsync();

            if (value == null)
            {
                return NotFound();
            }

            return value;
        }
    }
}
