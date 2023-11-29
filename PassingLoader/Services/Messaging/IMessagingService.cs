using VeloTimer.PassingLoader.Contracts;

namespace VeloTimer.PassingLoader.Services.Messaging
{
    public interface IMessagingService
    {
        public Task SendPassing(TrackPassingObserved passing);
        public Task SendPassings(IEnumerable<TrackPassingObserved> passings);
    }
}
