using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTime.Shared.Messaging
{
    public interface IMessagingService
    {
        Task SubmitPassing(PassingRegister passing);
        Task SubmitPassings(IEnumerable<PassingRegister> passings);
    }
}
