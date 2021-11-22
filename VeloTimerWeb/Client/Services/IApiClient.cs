﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Client.Services
{
    public interface IApiClient
    {
        Task<int> GetActiveRiderCount(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<IEnumerable<Rider>> GetActiveRiders(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<int> GetActiveTransponderCount(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<IEnumerable<Transponder>> GetActiveTransponders(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<IEnumerable<SegmentTimeRider>> GetBestTimes(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10, long? RiderId = null);
        Task<IEnumerable<Segment>> GetSegments(long TrackId);
        Task<IEnumerable<SegmentTimeRider>> GetSegmentTimes(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 50, long? RiderId = null);
        Task<IEnumerable<SegmentTimeRider>> GetSegmentTimesForTransponder(long SegmentId, long TransponderId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 50);
        Task<IEnumerable<SegmentTimeRider>> GetSegmentTimesForTransponders(long SegmentId, IEnumerable<long> TransponderIds, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 50);
        Task<Passing> GetLastPassing();
        Task<Rider> GetRiderByUserId(string userId);
    }
}
