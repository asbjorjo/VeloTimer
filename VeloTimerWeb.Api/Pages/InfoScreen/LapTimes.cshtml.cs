using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Pages.InfoScreen
{
    public class LapTimesModel : PageModel
    {
        private readonly VeloTimerDbContext _context;
        private readonly ITrackService _service;

        public Queue<SegmentTime> times { get; set; }
        
        public LapTimesModel(ITrackService trackService, VeloTimerDbContext context)
        {
            _service = trackService;
            _context = context;
        }

        public async Task<IActionResult> OnGetAsync(string Track, string Label)
        {
            var track = await _context.Set<Track>().FindAsync(long.Parse(Track));
            if (track == null)
            {
                return NotFound($"Track: {Track}");
            }

            var statsitem = await _context.Set<TrackStatisticsItem>().SingleOrDefaultAsync(x => x.Layout.Track == track && x.StatisticsItem.Label == Label);

            if (statsitem == null)
            {
                return NotFound($"StatisticsItem: {Label}");
            }

            ViewData["Title"] = $"{Label}";

            var seedtimes = await _service.GetRecent(statsitem, DateTimeOffset.Now.AddDays(-7), DateTimeOffset.MaxValue, 35);
            times = new Queue<SegmentTime>(seedtimes);

            return Page();
        }

        public async Task OnGetNewTimeAsync()
        {
            //var newtimes = await _segmentService.GetSegmentTimes(segment.Id, times.Last().PassingTime.AddTicks(1), DateTimeOffset.MaxValue, 35);

            //newtimes.ToList().ForEach(t => times.Enqueue(t));
        }
    }
}
