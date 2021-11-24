using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    public class TimingLoopsController : ControllerBase
    {
        private readonly ILogger<TimingLoopsController> _logger;
        private readonly VeloTimerDbContext _context;
        private readonly DbSet<TimingLoop> _dbset;

        public TimingLoopsController(VeloTimerDbContext context, ILogger<TimingLoopsController> logger) : base()
        {
            _logger = logger;
            _context = context;
            _dbset = _context.Set<TimingLoop>();
        }

        [AllowAnonymous]
        public async Task<ActionResult<TimingLoop>> Get(long id)
        {
            var value = await _dbset.FindAsync(id);

            if (value == null)
            {
                return NotFound();
            }

            return value;
        }
    }
}
