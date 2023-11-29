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

        public async Task SendPassing(TrackPassingObserved passing)
        {
            _logger.LogDebug("New passing");
            await _bus.Publish<TrackPassingObserved>(passing);
            _logger.LogDebug("Published");
        }

        public async Task SendPassings(IEnumerable<TrackPassingObserved> passings)
        {
            foreach (var passing in passings)
            {
                await SendPassing(passing);
            }
        }
    }
}
