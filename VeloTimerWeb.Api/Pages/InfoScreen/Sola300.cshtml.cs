﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using NuGet.Protocol.Providers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Data.Parameters;
using VeloTimer.Shared.Util;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.Riders;
using VeloTimerWeb.Api.Models.Statistics;
using VeloTimerWeb.Api.Models.Timing;
using VeloTimerWeb.Api.Models.TrackSetup;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Pages.InfoScreen
{
    public class Sola300Model : PageModel
    {
        private readonly ITrackService _service;
        private readonly VeloTimerDbContext _context;

        private readonly Dictionary<string, TimeParameters> periods = new Dictionary<string, TimeParameters>
        {
            { "sola300", new TimeParameters{
                FromTime = new DateTimeOffset(new DateTime(year: 2024, month: 12, day: 24, 0, 0, 0)).UtcDateTime, 
                ToTime = new DateTimeOffset(new DateTime(year: 2025, month: 1, day: 1, 0, 0, 0)).UtcDateTime } }
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

            var query = from p in _context.Set<Passing>()
                        join to in _context.Set<TransponderOwnership>() on p.Transponder equals to.Transponder
                        where 
                            p.Time >= fromdate && p.Time < todate 
                            && p.Loop.Track == track && p.Loop.Description == "Red"
                            && p.Time >= to.OwnedFrom && (to.OwnedUntil == null || to.OwnedUntil >= p.Time)
                            && to.Owner.IsPublic
                        group p by to.Owner into g
                        orderby g.Count() descending
                        select new SegmentDistance
                        {
                            Rider = g.Key.Name,
                            Count = g.Count(),
                            Distance = g.Count()*250/1000,
                        };

            Distances = await query.Take(24).ToListAsync();

            return Page();
        }
    }
}
