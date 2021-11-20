using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TrackController : GenericController<Track>
    {
        public TrackController(ILogger<GenericController<Track>> logger, VeloTimerDbContext context) : base(logger, context)
        {
        }

        [AllowAnonymous]
        public override Task<ActionResult<Track>> Get(long id)
        {
            return base.Get(id);
        }
    }
}
