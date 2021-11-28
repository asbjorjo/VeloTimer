using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Services
{
    public interface ITransponderService
    {
        Task<int> GetActiveCount(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<IEnumerable<double>> GetFastest(Transponder transponder, StatisticsItem statisticsItem, DateTimeOffset fromtime, DateTimeOffset? totime, int Count = 10);
        Task<IEnumerable<double>> GetFastestForOwner(Rider rider, StatisticsItem statisticsItem, DateTimeOffset fromtime, DateTimeOffset? totime, int Count = 10);
    }
}
