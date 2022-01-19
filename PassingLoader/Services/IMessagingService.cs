using VeloTimer.Shared.Models.Timing;

namespace VeloTimer.PassingLoader.Services
{
    public interface IMessagingService
    {
        Task SubmitPassing(PassingRegister passing);
        Task SubmitPassings(IEnumerable<PassingRegister> passings);
    }
}
