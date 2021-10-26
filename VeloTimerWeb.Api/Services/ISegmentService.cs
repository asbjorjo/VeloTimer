using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Services
{
    public interface ISegmentService
    {
        Task<IEnumerable<SegmentTimeRider>> GetSegmentTimesAsync(long segment, long? transponder, DateTime? fromtime, TimeSpan? period);
        Task<long> GetSegmentPassingCountAsync(long segment, long? transponder, DateTime? fromtime, TimeSpan? period);
    }
}
