﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Services
{
    public interface ITrackService
    {
        Task<IEnumerable<SegmentDistance>> GetCount(TrackStatisticsItem statisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10);
        Task<IEnumerable<SegmentTime>> GetFastest(IEnumerable<TrackStatisticsItem> statisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 10);
        Task<IEnumerable<SegmentTime>> GetRecent(TrackStatisticsItem statisticsItem, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 50);
        Task<IEnumerable<SegmentTime>> GetRecent(IEnumerable<TrackStatisticsItem> statisticsItems, DateTimeOffset FromTime, DateTimeOffset ToTime, int Count = 50);
    }
}
