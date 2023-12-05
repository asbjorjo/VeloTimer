using MassTransit;
using VeloTime.Timing.Contracts;
using VeloTime.Timing.Services;

namespace VeloTime.Timing.Consumers;

public class TrackPassingConsumer : IConsumer<TrackPassing>
{
    private readonly IMessagingService _messagingService;

    public TrackPassingConsumer(IMessagingService messagingService) => _messagingService = messagingService;

    public async Task Consume(ConsumeContext<TrackPassing> context)
    {
        await _messagingService.SendTrackPassing(context.Message);
    }
}
