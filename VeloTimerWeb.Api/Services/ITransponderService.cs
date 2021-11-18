using System;
using System.Threading.Tasks;

namespace VeloTimerWeb.Api.Services
{
    public interface ITransponderService
    {
        Task<int> GetActiveCount(DateTimeOffset fromtime, DateTimeOffset? totime);
    }
}
