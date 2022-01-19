using System.Threading.Tasks;
using VeloTimer.Shared.Models.Timing;

namespace VeloTimer.Shared.Services
{
    public interface IApiService
    {
        Task<PassingWeb> GetMostRecentPassing();
        Task<bool> RegisterPassing(PassingRegister passing);
    }
}
