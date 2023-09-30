using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTime.PassingLoader.Services.Api
{
    public interface IApiService
    {
        Task<PassingWeb?> GetMostRecentPassing();
        Task<PassingWeb?> GetMostRecentPassing(string Track);
        Task<bool> RegisterPassing(PassingRegister passing);
    }
}
