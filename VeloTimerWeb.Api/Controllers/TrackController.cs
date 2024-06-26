﻿using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Data.Models;
using VeloTimer.Shared.Data.Models.TrackSetup;
using VeloTimer.Shared.Data.Parameters;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Controllers
{
    public class TrackController : BaseController
    {
        private readonly ITrackService _trackService;
        private readonly IStatisticsService _statisticsService;

        public TrackController(
            IMapper mapper,
            ITrackService trackService,
            IStatisticsService statisticsService,
            ILogger<TrackController> logger) : base(mapper, logger)
        {
            _trackService = trackService;
            _statisticsService = statisticsService;
        }

        [AllowAnonymous]
        public async Task<ActionResult<TrackWeb>> Get(string slug)
        {
            var value = await _trackService.GetTrackBySlug(slug);

            if (value == null)
            {
                return NotFound();
            }

            return _mapper.Map<TrackWeb>(value);
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("{Track}/count/{StatisticsItem}")]
        public async Task<ActionResult<IEnumerable<SegmentDistance>>> Count(string Track, string StatisticsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10)
        {
            if (Track == null || StatisticsItem == null)
            {
                return BadRequest();
            }

            var statsitem = await _statisticsService.GetTrackItemsBySlugs(Track, StatisticsItem);
            if (!statsitem.Any())
            {
                return NotFound($"StatisticsItem: {StatisticsItem}");
            }

            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue) fromtime = FromTime.Value;
            if (ToTime.HasValue) totime = ToTime.Value;

            var counts = await _trackService.GetCount(statsitem, fromtime, totime, Count);

            return Ok(counts);
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("{Track}/fastest/{StatisticsItem}")]
        public async Task<IActionResult> Fastest(string Track, string StatisticsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10)
        {
            var statsitems = await _statisticsService.GetTrackItemsBySlugs(Track, StatisticsItem);

            if (!statsitems.Any())
            {
                return NotFound($"StatisticsItem: {StatisticsItem}");
            }

            var fromtime = DateTimeOffset.MinValue;
            var totime = DateTimeOffset.MaxValue;

            if (FromTime.HasValue) fromtime = FromTime.Value;
            if (ToTime.HasValue) totime = ToTime.Value;

            var times = await _trackService.GetFastest(statsitems, fromtime, totime, Count);

            return Ok(times);
        }

        [HttpGet]
        [Route("{Track}/times/{StatisticsItem}")]
        public async Task<IActionResult> Recent(
            string Track,
            string StatisticsItem,
            DateTimeOffset? FromTime,
            int Count = 10,
            string orderBy = "passingtime:desc",
            bool IncludeIntermediate = true)
        {
            var statsitems = await _statisticsService.GetTrackItemsBySlugs(Track, StatisticsItem);

            if (!statsitems.Any())
            {
                return NotFound($"StatisticsItem: {StatisticsItem}");
            }

            var fromtime = DateTimeOffset.MaxValue;
            if (FromTime.HasValue) fromtime = FromTime.Value;

            var times = await _trackService.GetRecent(statsitems, fromtime, orderBy, Count, IncludeIntermediate);

            return Ok(times);
        }
    }
}
