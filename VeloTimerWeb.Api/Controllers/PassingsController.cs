using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

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
            var value = await _dbset.OrderBy(p => p.Source).LastOrDefaultAsync();
            
            if (value == null)
            {
                NotFound();
            }

            return value;
        }
    }
}
