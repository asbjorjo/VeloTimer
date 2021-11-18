using System;
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
        Task<Passing> GetLastPassing();
        Task<Rider> GetRiderByUserId(string userId);
    }
}
