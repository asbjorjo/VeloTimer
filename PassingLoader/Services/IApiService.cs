using VeloTimer.Shared.Models.Timing;

namespace VeloTimer.PassingLoader.Services
{
    public interface IApiService
    {
        Task<PassingWeb?> GetMostRecentPassing();
        Task<bool> RegisterPassing(PassingRegister passing);
    }
}
