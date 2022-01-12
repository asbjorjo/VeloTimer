using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.TrackSetup;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Pages.InfoScreen
{
    public class RecordsModel : PageModel
    {
        private readonly ITrackService _service;
        private readonly VeloTimerDbContext _context;

        private readonly string[] items = { "Runde", "200m", "3000m", "4000m" };

        public Dictionary<string, IEnumerable<SegmentTime>> Times { get; set; } = new Dictionary<string, IEnumerable<SegmentTime>>();

        public RecordsModel(ITrackService trackService, VeloTimerDbContext context)
        {
            _service = trackService;
            _context = context;
        }

        public async Task<IActionResult> OnGetAsync(string Track, DateTime? FromDate, DateTime? ToDate)
        {
            ViewData["Title"] = "Rekorder";

            var track = await _context.Set<Track>().FindAsync(long.Parse(Track));
            if (track == null)
            {
                return NotFound($"Track: {Track}");
            }

            var fromdate = DateTimeOffset.MinValue;
            var todate = DateTimeOffset.MaxValue;

            foreach (var item in items)
            {
                var time = await _service.GetFastest(track, item, fromdate, todate, 5);
                Times.Add(item, time);
            }

            return Page();
        }
    }
}
