using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    public class TranspondersController : GenericController<Transponder>
    {
        public TranspondersController(ILogger<GenericController<Transponder>> logger, ApplicationDbContext context) : base(logger, context)
        {
        }
    }
}
