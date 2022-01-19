using System.Threading.Tasks;
using VeloTimer.Shared.Models.Timing;

namespace VeloTimer.Shared.Services
{
    public interface IApiService
    {
        Task<PassingWeb> GetMostRecentPassing();
        Task<PassingWeb> GetMostRecentPassing(string Track);
        Task<bool> RegisterPassing(PassingRegister passing);
    }
}
