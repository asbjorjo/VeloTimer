﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimer.Shared.Util;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.Statistics;

namespace VeloTimerWeb.Api.Controllers
{
    public class DashboardsController : BaseController
    {
        private readonly VeloTimerDbContext _context;

        public DashboardsController(
            VeloTimerDbContext context,
            ILogger<DashboardsController> logger) : base(logger)
        {
            _context = context;
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        [Route("admin")]
        public async Task<IActionResult> GetAdminData()
        {
            var dashData = new AdminDashboardModel();

            var fromDate = DateTimeOffset.Now.AddDays(-30).StartOfDay().UtcDateTime;

            var qDailyCounts = _context.Set<TransponderStatisticsItem>()
                .Where(x => x.StatisticsItem.StatisticsItem.IsLapCounter)
                .Where(x => x.StartTime > fromDate)
                .GroupBy(x => new { Date = x.StartTime.Date })
                .OrderBy(x => x.Key.Date)
                .Select(x => new { Date = x.Key.Date, Count = x.Count()});

            var qRiderCounts = from tsi in _context.Set<TransponderStatisticsItem>()
                               where tsi.StatisticsItem.StatisticsItem.IsLapCounter && tsi.StartTime > fromDate
                               group tsi by new {Date = tsi.StartTime.Date, Transponder = tsi.Transponder.Id} into days
                               orderby days.Key.Date ascending
                               select new {
                                   Date = days.Key.Date,
                                   Transponder = days.Key.Transponder
                               };

            _logger.LogInformation(qDailyCounts.ToQueryString());
            _logger.LogInformation(qRiderCounts.ToQueryString());

            var dailyCounts = await qDailyCounts.ToListAsync();
            var riderCounts = await qRiderCounts.ToListAsync();

            dashData.Labels = dailyCounts.Select(x => new DateTimeOffset(x.Date)).ToList();
            dashData.LapCounts = dailyCounts.Select(x => Convert.ToDouble(x.Count)).ToList();
            dashData.RiderCounts = riderCounts.GroupBy(x => x.Date).Select(x => Convert.ToDouble(x.Count())).ToList();

            return Ok(dashData);
        }
    }
}
