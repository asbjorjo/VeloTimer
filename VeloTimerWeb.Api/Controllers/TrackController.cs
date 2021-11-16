using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class TrackController : GenericController<Track>
    {
        public TrackController(ILogger<GenericController<Track>> logger, VeloTimerDbContext context) : base(logger, context)
        {
        }
    }
}
