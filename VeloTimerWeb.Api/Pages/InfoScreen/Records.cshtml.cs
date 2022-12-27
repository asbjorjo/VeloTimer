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
using VeloTimer.Shared.Util;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.Statistics;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Pages.InfoScreen
{
    public class RecordsModel : PageModel
    {
        private const int SeasonStartMth = 9;
        private const int SeasonEndMth = 3;

        private readonly ITrackService _service;
        private readonly VeloTimerDbContext _context;

        private readonly string[] items = { "Runde", "200m", "3000m", "4000m" };
        private readonly Dictionary<string, TimeParameters> periods = new Dictionary<string, TimeParameters>
        {
            { "alltime", new TimeParameters() },
            { "day", new TimeParameters{ FromTime = DateTimeOffset.Now.StartOfDay().UtcDateTime } },
            { "month", new TimeParameters{ FromTime = DateTimeOffset.Now.StartOfMonth().UtcDateTime } },
            { "season", new TimeParameters{
                FromTime = DateTimeOffset.Now.StartOfYear().AddMonths(SeasonStartMth-1).UtcDateTime, 
                ToTime = DateTimeOffset.Now.StartOfYear().AddYears(1).AddMonths(SeasonEndMth-1).EndOfMonth().UtcDateTime } },
            { "prevseason", new TimeParameters{
                FromTime = DateTimeOffset.Now.StartOfYear().AddYears(-1).AddMonths(SeasonStartMth-1).UtcDateTime,
                ToTime = DateTimeOffset.Now.StartOfYear().AddMonths(SeasonEndMth-1).EndOfMonth().UtcDateTime } }
            //{ "year", new TimeParameters{ FromTime = DateTimeOffset.Now.StartOfYear().UtcDateTime } }
        };
        private readonly Dictionary<string, string> titles = new Dictionary<string, string>
        {
            { "alltime", "Rekorder" },
            { "day", "Best i dag" },
            { "month", $"Best i {DateTimeOffset.Now.ToString("MMMM", CultureInfo.GetCultureInfo("nb-NO"))}" },
            { "season", $"Sesong {DateTimeOffset.Now.ToString("yyyy")}-{DateTimeOffset.Now.AddYears(1).ToString("yyyy")}" },
            { "prevseason", $"Sesong {DateTimeOffset.Now.AddYears(-1).ToString("yyyy")}-{DateTimeOffset.Now.ToString("yyyy")}" }
            //{ "year", $"Best i {DateTimeOffset.Now.ToString("yyyy")}" }
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
            string periodKey = periods.ContainsKey(Period) ? Period : periods.First().Key;

            ViewData["Title"] = titles[periodKey];

            var track = await _service.GetTrackBySlug(Track);
            if (track == null)
            {
                return NotFound($"Track: {Track}");
            }

            var period = periods[periodKey];

            var fromdate = period.FromTime;
            var todate = period.ToTime;

            foreach (var item in items)
            {
                var time = await _service.GetFastest(track, item, fromdate, todate, 5);
                Times.Add(item, time);
            }
            var counter = await _context.Set<TrackStatisticsItem>().Where(x => x.Layout.Track == track && x.StatisticsItem.IsLapCounter).ToListAsync();
            if (counter != null)
            {
                Distances = await _service.GetCount(counter, fromdate, todate, 3);
            }
            
            var scheme = Request.Scheme;
            var host = Request.Host.ToUriComponent();
            var path = Request.Path.ToUriComponent();
            string newPeriod;
            
            var periodEnumerator = periods.GetEnumerator();

            while (periodEnumerator.MoveNext() && periodEnumerator.Current.Key != periodKey)
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
