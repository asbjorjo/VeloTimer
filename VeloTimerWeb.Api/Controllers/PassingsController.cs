using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimerWeb.Api.Data;
using VeloTimer.Shared.Models;
using Microsoft.Extensions.Logging;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("[controller]")]
    public class PassingsController : GenericController<Passing>
    {
        public PassingsController(ILogger<GenericController<Passing>> logger, ApplicationDbContext context) : base(logger, context)
        { 
        }

        [Route("mostrecent")]
        [HttpGet]
        public async Task<ActionResult<Passing>> GetMostRecent()
        {
            return await _dbset.OrderByDescending(p => p.Time).FirstOrDefaultAsync();
        }
    }
}
