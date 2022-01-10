using System.Threading.Tasks;
using VeloTimer.AmmcLoad.Models;
using VeloTimer.Shared.Models.Timing;

namespace VeloTimer.AmmcLoad.Services
{
    public interface IApiService
    {
        Task<PassingWeb> GetMostRecentPassing();
        Task<bool> RegisterPassing(PassingAmmc passing);
    }
}
