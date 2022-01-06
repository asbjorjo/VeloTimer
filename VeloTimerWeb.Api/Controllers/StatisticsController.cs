﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StatisticsController : ControllerBase
    {
        private readonly IStatisticsService _service;
        private readonly ILogger<StatisticsController> _logger;
        private readonly VeloTimerDbContext _context;

        public StatisticsController(IStatisticsService statisticsService, ILogger<StatisticsController> logger, VeloTimerDbContext context)
        {
            _service = statisticsService;
            _logger = logger;
            _context = context;
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("topdistance")]
        public async Task<ActionResult<IEnumerable<KeyValuePair<string, double>>>> Count(DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10)
        {
            var statsitem = await _context.Set<StatisticsItem>().SingleOrDefaultAsync(x => x.IsLapCounter);
            if (statsitem == null)
            {
                return NotFound();
            }

            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue) fromtime = FromTime.Value;
            if (ToTime.HasValue) totime = ToTime.Value;

            var counts = await _service.GetTopDistances(statsitem, fromtime, totime, Count);

            return Ok(counts);
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("fastest/{StatisticsItem}")]
        public async Task<ActionResult<IEnumerable<SegmentTime>>> Fastest(string StatisticsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10)
        {
            var statsitem = await _context.Set<StatisticsItem>()
                .SingleOrDefaultAsync(x => x.Label == StatisticsItem);

            if (statsitem == null)
            {
                return NotFound($"StatisticsItem: {StatisticsItem}");
            }

            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue) fromtime = FromTime.Value;
            if (ToTime.HasValue) totime = ToTime.Value;

            var times = await _service.GetBestTimes(statsitem, fromtime, totime, Count);

            return Ok(times);
        }
    }
}
