using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Services
{
    public interface IRiderService
    {
        Task<IEnumerable<Rider>> GetActive(DateTimeOffset fromtime, DateTimeOffset? totime);
        Task<int> GetActiveCount(DateTimeOffset fromtime, DateTimeOffset? totime);
    }
}
