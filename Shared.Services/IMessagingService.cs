using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.Shared.Services
{
    public interface IMessagingService
    {
        Task SubmitPassing(PassingRegister passing);
        Task SubmitPassings(IEnumerable<PassingRegister> passings);
    }
}
