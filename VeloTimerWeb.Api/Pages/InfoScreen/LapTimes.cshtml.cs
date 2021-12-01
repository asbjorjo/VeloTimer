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

        //public Queue<SegmentTimeRider> times { get; set; }
        
        public LapTimesModel(VeloTimerDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> OnGetAsync(string SegmentLabel)
        {
            //segment = await _context.Set<Segment>().SingleOrDefaultAsync(s => s.Label == SegmentLabel);

            //if (segment == null)
            //{
            //    return NotFound();
            //}

            //ViewData["Title"] = SegmentLabel;

            //var seedtimes = await _segmentService.GetSegmentTimes(segment.Id, DateTimeOffset.Now.AddDays(-1), null, 35);
            //times = new Queue<SegmentTimeRider>(seedtimes);

            return Page();
        }

        public async Task OnGetNewTimeAsync()
        {
            //var newtimes = await _segmentService.GetSegmentTimes(segment.Id, times.Last().PassingTime.AddTicks(1), DateTimeOffset.MaxValue, 35);

            //newtimes.ToList().ForEach(t => times.Enqueue(t));
        }
    }
}
