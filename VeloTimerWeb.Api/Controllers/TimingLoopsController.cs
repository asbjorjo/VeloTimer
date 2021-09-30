using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("[controller]")]
    public class TimingLoopsController : GenericController<TimingLoop>
    {
        public TimingLoopsController(ApplicationDbContext context, ILogger<GenericController<TimingLoop>> logger) : base(logger, context)
        {
        }
    }
}
