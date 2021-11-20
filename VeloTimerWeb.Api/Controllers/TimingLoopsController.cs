using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    public class TimingLoopsController : GenericController<TimingLoop>
    {
        public TimingLoopsController(VeloTimerDbContext context, ILogger<GenericController<TimingLoop>> logger) : base(logger, context)
        {
        }

        [AllowAnonymous]
        public override Task<ActionResult<TimingLoop>> Get(long id)
        {
            return base.Get(id);
        }
    }
}
