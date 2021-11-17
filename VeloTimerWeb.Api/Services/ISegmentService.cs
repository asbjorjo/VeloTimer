using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Services
{
    public interface ISegmentService
    {
        Task<IEnumerable<SegmentTimeRider>> GetFastestSegmentTimes(long segmentId, long? transponderId, DateTimeOffset? fromtime, DateTimeOffset? totime, int? count, bool requireintermediates);
        Task<IEnumerable<SegmentTimeRider>> GetFastestSegmentTimesNewWay(long SegmentId, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10, bool RequireIntermediates = true);
        Task<IEnumerable<SegmentTimeRider>> GetSegmentTimesNew(long segment, DateTimeOffset? fromtime, DateTimeOffset? totime, int Count = 50);
        Task<IEnumerable<SegmentTimeRider>> GetSegmentTimes(long segment, long? transponder, DateTimeOffset? fromtime, DateTimeOffset? totime);
        Task<IEnumerable<KeyValuePair<string, long>>> GetSegmentPassingCount(long segment, long? transponder, DateTimeOffset? fromtime, DateTimeOffset? totime, int? count);
    }
}
