using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TrackController : ControllerBase
    {
        private ILogger<TrackController> _logger;
        private VeloTimerDbContext _context;
        private DbSet<Track> _dbset;

        public TrackController(ILogger<TrackController> logger, VeloTimerDbContext context) : base()
        {
            _logger = logger;
            _context = context;
            _dbset = _context.Set<Track>();
        }

        [AllowAnonymous]
        public async Task<ActionResult<Track>> Get(long id)
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
