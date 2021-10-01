using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Controllers
{
    public class TransponderNamesController : GenericController<TransponderName>
    {
        public TransponderNamesController(ILogger<GenericController<TransponderName>> logger,
                                          ApplicationDbContext context) : base(logger, context)
        {
        }

        public override Task<ActionResult> Create(IEnumerable<TransponderName> values)
        {
            throw new NotImplementedException($"Batch creation of {nameof(TransponderName)} not supported");
        }
    }
}
