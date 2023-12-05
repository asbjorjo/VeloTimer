using MassTransit;
using VeloTime.Timing.Agents.EventSubmitter.Services;
using VeloTime.Timing.Contracts;

namespace VeloTime.Timing.Agents.EventSubmitter.Consumers;

public class TrackPassingConsumer : IConsumer<TrackPassing>
{
    private readonly IExternalMessagingService _messagingService;

    public TrackPassingConsumer(IExternalMessagingService messagingService) => _messagingService = messagingService;

    public async Task Consume(ConsumeContext<TrackPassing> context)
    {
        await _messagingService.SendTrackPassing(context.Message);
    }
}
