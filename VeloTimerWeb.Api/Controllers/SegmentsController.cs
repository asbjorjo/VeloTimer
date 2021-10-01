using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    public class SegmentsController : GenericController<Segment>
    {
        public SegmentsController(ILogger<GenericController<Segment>> logger,
                                  ApplicationDbContext context) : base(logger, context)
        { 
        }
    }
}
