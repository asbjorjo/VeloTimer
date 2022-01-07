using Microsoft.AspNetCore.Authorization;
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
using VeloTimerWeb.Api.Models;

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
                .Where(x => x.EndTime > fromDate)
                .GroupBy(x => new { Date = x.EndTime.Date })
                .OrderBy(x => x.Key.Date)
                .Select(x => new { Date = x.Key.Date, Count = x.Count() });

            var dailyCounts = await qDailyCounts.ToListAsync();

            dashData.Labels = dailyCounts.Select(x => new DateTimeOffset(x.Date)).ToList();
            dashData.LapCounts = dailyCounts.Select(x => Convert.ToDouble(x.Count)).ToList();

            return Ok(dashData);
        }
    }
}
