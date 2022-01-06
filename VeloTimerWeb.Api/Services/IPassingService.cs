using System.Threading.Tasks;
using VeloTimerWeb.Api.Models;
using static VeloTimer.Shared.Models.TransponderType;

namespace VeloTimerWeb.Api.Services
{
    public interface IPassingService
    {
        Task<Passing> RegisterNew(Passing passing, TimingSystem? timingSystem, string transponderId);
    }
}
