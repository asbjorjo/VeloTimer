using MediatR;
using VeloTimer.PassingLoader.Commands;
using VeloTimer.PassingLoader.Contracts;
using VeloTimer.PassingLoader.Services.Messaging;

namespace VeloTimer.PassingLoader.Handlers;
public class SendTrackPassingHandler : IRequestHandler<SendTrackPassing>
{
    private readonly IMessagingService _messagingService;

    public SendTrackPassingHandler(IMessagingService messagingService) => _messagingService = messagingService;

    public async Task Handle(SendTrackPassing request, CancellationToken cancellationToken)
    {
        TrackPassingObserved passing = new TrackPassingObserved
        {
            Track = request.TrackPassing.Track,
            Time = request.TrackPassing.Time,
            TimingSystem = request.TrackPassing.TimingSystem,
            PassingPoint = request.TrackPassing.PassingPoint,
            Source = request.TrackPassing.Source,
            Transponder = request.TrackPassing.Transponder,
            LowBattery = request.TrackPassing.LowBattery,
            LowSignal = request.TrackPassing.LowSignal
        };

        await _messagingService.SendPassing(passing);
    }
}
