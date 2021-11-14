using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using static VeloTimer.Shared.Models.TransponderType;

namespace VeloTimerWeb.Api.Services
{
    public interface IPassingService
    {
        Task<Passing> RegisterNew(Passing passing, TimingSystem? timingSystem, string transponderId);
    }
}
