﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTime.Services;
using VeloTime.Storage.Data;
using VeloTime.Storage.Models.Statistics;
using VeloTime.Storage.Models.TrackSetup;
using VeloTimer.Shared.Data.Models;

namespace VeloTimerWeb.Api.Pages.InfoScreen
{
    public class LapTimesModel : PageModel
    {
        private readonly VeloTimerDbContext _context;
        private readonly ITrackService _service;

        public Queue<SegmentTime> Times { get; set; }
        public bool HasSplit { get; set; } = false;

        public LapTimesModel(ITrackService trackService, VeloTimerDbContext context)
        {
            _service = trackService;
            _context = context;
        }

        public async Task<IActionResult> OnGetAsync(string Track, string Label)
        {
            Track track;

            if (long.TryParse(Track, out var trackId))
            {
                track = await _context.Set<Track>().FindAsync(long.Parse(Track));
            }
            else {
                track = await _service.GetTrackBySlug(Track);
            }

            if (track == null)
            {
                return NotFound($"Track: {Track}");
            }

            var statsitem = await _context.Set<TrackStatisticsItem>().Where(x => x.Layout.Track == track && x.Layout.Active && x.StatisticsItem.Label == Label).ToListAsync();

            if (statsitem == null)
            {
                return NotFound($"StatisticsItem: {Label}");
            }

            ViewData["Title"] = $"{Label}";

            var timeParameters = new TimeParameters();
            timeParameters.FromTime = DateTime.Now.AddDays(-7);
            timeParameters.ToTime = DateTime.MaxValue;

            var paginationParameters = new PaginationParameters();
            paginationParameters.PageSize = 35;

            var seedtimes = await _service.GetRecent(statsitem, timeParameters, paginationParameters, "passingtime:desc");
            Times = new Queue<SegmentTime>(seedtimes);

            HasSplit = Times.FirstOrDefault()?.Intermediates.Count() == 2;

            return Page();
        }
    }
}
