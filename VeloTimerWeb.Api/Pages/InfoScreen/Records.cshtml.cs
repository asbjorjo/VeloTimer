using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimer.Shared.Util;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.Statistics;
using VeloTimerWeb.Api.Models.TrackSetup;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Pages.InfoScreen
{
    public class RecordsModel : PageModel
    {
        private readonly ITrackService _service;
        private readonly VeloTimerDbContext _context;

        private readonly string[] items = { "Runde", "200m", "3000m", "4000m" };
        private readonly Dictionary<string, TimeParameters> periods = new Dictionary<string, TimeParameters>
        {
            { "alltime", new TimeParameters() },
            { "day", new TimeParameters{ FromTime = DateTimeOffset.Now.StartOfDay().UtcDateTime } },
            { "month", new TimeParameters{ FromTime = DateTimeOffset.Now.StartOfMonth().UtcDateTime } },
            { "year", new TimeParameters{ FromTime = DateTimeOffset.Now.StartOfYear().UtcDateTime } }
        };
        private readonly Dictionary<string, string> titles = new Dictionary<string, string>
        {
            { "alltime", "Rekorder" },
            { "day", "Bestetider i dag" },
            { "month", $"Bestetider i {DateTimeOffset.Now.ToString("MMMM", CultureInfo.GetCultureInfo("nb-NO"))}" },
            { "year", $"Bestetider i {DateTimeOffset.Now.ToString("yyyy")}" }
        };

        public Dictionary<string, IEnumerable<SegmentTime>> Times { get; set; } = new Dictionary<string, IEnumerable<SegmentTime>>();
        public IEnumerable<SegmentDistance> Distances { get; set; } = new List<SegmentDistance>();
        public string nextRecord = string.Empty;

        public RecordsModel(ITrackService trackService, VeloTimerDbContext context)
        {
            _service = trackService;
            _context = context;
        }

        public async Task<IActionResult> OnGetAsync(string Track, string Period)
        {
            ViewData["Title"] = titles[Period];

            var track = await _service.GetTrackBySlug(Track);
            if (track == null)
            {
                return NotFound($"Track: {Track}");
            }

            var period = periods[Period];

            var fromdate = period.FromTime;
            var todate = period.ToTime;

            foreach (var item in items)
            {
                var time = await _service.GetFastest(track, item, fromdate, todate, 5);
                Times.Add(item, time);
            }
            var counter = _context.Set<TrackStatisticsItem>().SingleOrDefault(x => x.Layout.Track == track && x.StatisticsItem.IsLapCounter);
            if (counter != null)
            {
                Distances = await _service.GetCount(counter, fromdate, todate, 5);
            }
            
            var scheme = Request.Scheme;
            var host = Request.Host.ToUriComponent();
            var path = Request.Path.ToUriComponent();
            string newPeriod;
            
            var periodEnumerator = periods.GetEnumerator();

            while (periodEnumerator.MoveNext() && periodEnumerator.Current.Key != Period)
            {               
            }
            if (periodEnumerator.MoveNext())
            {
                newPeriod = periodEnumerator.Current.Key;
            } else
            {
                newPeriod = periods.First().Key;
            }

            var newPath = path.Replace(Period, newPeriod);

            nextRecord = $"{scheme}://{host}{newPath}";

            return Page();
        }
    }
}
