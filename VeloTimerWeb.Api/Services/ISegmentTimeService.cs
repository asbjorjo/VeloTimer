using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Services
{
    public interface ISegmentTimeService
    {
        Task<IEnumerable<LapTime>> GetSegmentTimesAsync(long start, long finish, long? transponder);
    }
}
