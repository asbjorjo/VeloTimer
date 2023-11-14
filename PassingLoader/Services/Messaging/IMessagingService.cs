using VeloTimer.PassingLoader.Contracts;

namespace VeloTimer.PassingLoader.Services.Messaging
{
    public interface IMessagingService
    {
        public void RegisterPassing(PassingObserved passing);
        public void RegisterPassings(IEnumerable<PassingObserved> passings);
    }
}
