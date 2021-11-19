﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Services
{
    public interface ISegmentService
    {
        Task<IEnumerable<SegmentTimeRider>> GetFastestSegmentTimes(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10, bool RequireIntermediates = true);
        Task<IEnumerable<SegmentTimeRider>> GetSegmentTimes(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 50);
        Task<IEnumerable<KeyValuePair<string, long>>> GetSegmentPassingCount(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10);
    }
}
