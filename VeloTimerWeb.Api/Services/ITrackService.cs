using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Services
{
    public interface ITrackService
    {
        Task<IEnumerable<SegmentTime>> GetFastest(StatisticsItem statisticsItem, DateTimeOffset? FromTime, DateTimeOffset? ToTime, int Count = 10);
    }
}
