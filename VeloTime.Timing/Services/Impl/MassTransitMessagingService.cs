using MassTransit;
using Microsoft.Extensions.Logging;
using VeloTime.Timing.Contracts;

namespace VeloTime.Timing.Services.Impl;

public class MassTransitMessagingService : IMessagingService
{
    private readonly IBus _messageBus;
    private readonly ILogger<MassTransitMessagingService> _logger;

    public MassTransitMessagingService(IBus messageBus, ILogger<MassTransitMessagingService> logger)
    {
        _messageBus = messageBus;
        _logger = logger;
    }

    public async Task SendStartLoadingFrom(StartLoadingFrom startLoadingFrom)
    {
        await _messageBus.Send(startLoadingFrom);
    }

    public async Task SendTrackPassing(TrackPassing passing)
    {
        await _messageBus.Publish(passing);
    }

    public async Task SendTrackPassings(IEnumerable<TrackPassing> passings)
    {
        foreach (var passing in passings)
        {
            await SendTrackPassing(passing);
        }
    }
}
