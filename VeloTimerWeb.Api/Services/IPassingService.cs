using System.Threading.Tasks;
using VeloTimerWeb.Api.Models.Timing;
using static VeloTimer.Shared.Models.Timing.TransponderType;

namespace VeloTimerWeb.Api.Services
{
    public interface IPassingService
    {
        Task<Passing> RegisterNew(Passing passing, TimingSystem? timingSystem, string transponderId);
    }
}
