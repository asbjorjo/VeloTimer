using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Data.Parameters;
using VeloTime.Services;
using VeloTime.Storage.Data;
using VeloTime.Storage.Models.Statistics;

namespace VeloTimerWeb.Api.Pages.InfoScreen
{
    public class Sola300Model : PageModel
    {
        private readonly ITrackService _service;
        private readonly VeloTimerDbContext _context;

        private readonly Dictionary<string, TimeParameters> periods = new Dictionary<string, TimeParameters>
        {
            { "sola300", new TimeParameters{
                FromTime = new DateTimeOffset(new DateTime(year: 2022, month: 12, day: 24, 0, 0, 0)).UtcDateTime, 
                ToTime = new DateTimeOffset(new DateTime(year: 2023, month: 1, day: 1, 0, 0, 0)).AddTicks(-1).UtcDateTime } }
        };
        private readonly Dictionary<string, string> titles = new Dictionary<string, string>
        {
            { "sola300", "Sola 300 Challenge" }
        };

        public Dictionary<string, IEnumerable<SegmentTime>> Times { get; set; } = new Dictionary<string, IEnumerable<SegmentTime>>();
        public IEnumerable<SegmentDistance> Distances { get; set; } = new List<SegmentDistance>();
        public string nextRecord = string.Empty;

        public Sola300Model(ITrackService trackService, VeloTimerDbContext context)
        {
            _service = trackService;
            _context = context;
        }

        public async Task<IActionResult> OnGetAsync() {
            string Period = "sola300";
            string Track = "sola-arena";

            string periodKey = periods.ContainsKey(Period) ? Period : periods.First().Key;

            var track = await _service.GetTrackBySlug(Track);
            if (track == null)
            {
                return NotFound($"Track: {Track}");
            }

            var period = periods[periodKey];

            var fromdate = period.FromTime;
            var todate = period.ToTime;

            var counter = await _context.Set<TrackStatisticsItem>().Where(x => x.Layout.Track == track && x.StatisticsItem.IsLapCounter).ToListAsync();
            if (counter != null)
            {
                Distances = await _service.GetCount(counter, fromdate, todate, 24);
            }
            
            return Page();
        }
    }
}
