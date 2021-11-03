using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Services
{
    public interface ISegmentService
    {
        Task<IEnumerable<SegmentTimeRider>> GetFastestSegmentTime(long segmentId, long? transponderId, DateTimeOffset? fromtime, DateTimeOffset? totime, int? count, bool requireintermediates);
        Task<IEnumerable<SegmentTimeRider>> GetSegmentTimesAsync(long segment, long? transponder, DateTimeOffset? fromtime, DateTimeOffset? totime);
        Task<IEnumerable<KeyValuePair<string, long>>> GetSegmentPassingCountAsync(long segment, long? transponder, DateTimeOffset? fromtime, DateTimeOffset? totime);
    }
}
