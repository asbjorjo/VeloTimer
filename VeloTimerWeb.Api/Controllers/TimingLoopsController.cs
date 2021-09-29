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
    public class TimingLoopsController : GenericController<TimingLoop>
    {
        public TimingLoopsController(ApplicationDbContext context, ILogger<GenericController<TimingLoop>> logger) : base(logger, context)
        {
        }
    }
}
