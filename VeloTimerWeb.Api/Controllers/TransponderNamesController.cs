using Microsoft.AspNetCore.Http;
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

        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public override async Task<ActionResult<TransponderName>> Create(TransponderName value)
        {
            _logger.LogInformation($"Transponder: {value.TransponderId}" +
                $" - Name: {value.Name}" +
                $" - Validity: {value.ValidFrom}-{value.ValidUntil}");

            var existing = _dbset.Where(tn => (value.ValidFrom >= tn.ValidFrom && value.ValidFrom <= tn.ValidUntil)
                                              || (value.ValidUntil >= tn.ValidFrom && value.ValidUntil <= tn.ValidUntil)
                                              || (value.ValidFrom <= tn.ValidFrom && value.ValidUntil >= tn.ValidUntil));

            if (existing.Any())
            {
                return Conflict();
            }

            return await base.Create(value);
        }

        public override Task<ActionResult> Create(IEnumerable<TransponderName> values)
        {
            throw new NotImplementedException($"Batch creation of {nameof(TransponderName)} not supported");
        }
    }
}
