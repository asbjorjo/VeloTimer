using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using VeloTime.Storage.Data;
using VeloTime.Storage.Models.Riders;
using VeloTime.Storage.Models.Statistics;
using VeloTime.Storage.Models.Timing;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Util;

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
                .GroupBy(x => new { x.StartTime.Date })
                .OrderBy(x => x.Key.Date)
                .Select(x => new { x.Key.Date, Count = x.Count()});

            var qRiderCounts = from tsi in _context.Set<TransponderStatisticsItem>()
                               where tsi.StatisticsItem.StatisticsItem.IsLapCounter && tsi.StartTime > fromDate
                               group tsi by new { tsi.StartTime.Date, Transponder = tsi.Transponder.Id } into days
                               orderby days.Key.Date ascending
                               select new {
                                   days.Key.Date,
                                   days.Key.Transponder
                               };

            //_logger.LogInformation(qDailyCounts.ToQueryString());
            //_logger.LogInformation(qRiderCounts.ToQueryString());

            var dailyCounts = await qDailyCounts.ToListAsync();
            var riderCounts = await qRiderCounts.ToListAsync();

            dashData.RiderCount = await _context.Set<Rider>().CountAsync();
            dashData.RiderPublicCount = await _context.Set<Rider>().Where(x => x.IsPublic).CountAsync();
            dashData.RiderWithTransponderCount = await _context.Set<TransponderOwnership>().Select(x => x.Owner).Distinct().CountAsync();
            dashData.TransponderCount = await _context.Set<Transponder>().CountAsync();
            dashData.TransponderWithOwnerCount = await _context.Set<TransponderOwnership>().Select(x => x.Transponder).Distinct().CountAsync();
            dashData.LapCount = dailyCounts.Sum(x => x.Count);
            dashData.PassingCount = await _context.Set<Passing>().Where(x => x.Time > fromDate).CountAsync();
            dashData.Labels = dailyCounts.Select(x => new DateTimeOffset(x.Date)).ToList();
            dashData.LapCounts = dailyCounts.Select(x => Convert.ToDouble(x.Count)).ToList();
            dashData.RiderCounts = riderCounts.GroupBy(x => x.Date).Select(x => Convert.ToDouble(x.Count())).ToList();

            return Ok(dashData);
        }
    }
}
