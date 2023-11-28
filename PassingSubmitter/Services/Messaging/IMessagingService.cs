using VeloTimer.PassingLoader.Contracts;

namespace VeloTimer.PassingLoader.Services.Messaging
{
    public interface IMessagingService
    {
        public void RegisterPassing(RawPassingObserved passing);
        public void RegisterPassings(IEnumerable<RawPassingObserved> passings);
    }
}
