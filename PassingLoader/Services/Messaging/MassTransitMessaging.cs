using MassTransit;
using Microsoft.Extensions.Logging;
using VeloTimer.PassingLoader.Contracts;

namespace VeloTimer.PassingLoader.Services.Messaging
{
    public class MassTransitMessaging : IMessagingService
    {
        private readonly IBus _bus;
        private readonly ILogger _logger;

        public MassTransitMessaging(IBus bus, ILogger<MassTransitMessaging> logger)
        {
            _bus = bus;
            _logger = logger;
        }

        public void RegisterPassing(PassingObserved passing)
        {
            _logger.LogDebug("New passing");
            _bus.Publish(passing);
            _logger.LogDebug("Published");
        }

        public void RegisterPassings(IEnumerable<PassingObserved> passings)
        {
            foreach (var passing in passings)
            {
                RegisterPassing(passing);
            }
        }
    }
}
