using MediatR;
using VeloTimer.PassingLoader.Commands;
using VeloTimer.PassingLoader.Services.Messaging;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.PassingLoader.Handlers;
public class RegisterTrackPassingHandler : IRequestHandler<RegisterTrackPassing>
{
    private readonly IExternalMessagingService _messagingService;

    public RegisterTrackPassingHandler(IExternalMessagingService messagingService) => _messagingService = messagingService;

    public async Task Handle(RegisterTrackPassing request, CancellationToken cancellationToken)
    {
        PassingRegister passing = new PassingRegister
        {
            Track = request.TrackPassing.Track,
            Time = request.TrackPassing.Time,
            TransponderId = request.TrackPassing.Transponder,
            LoopId = request.TrackPassing.PassingPoint,
            Source = request.TrackPassing.Source,
            TimingSystem = request.TrackPassing.TimingSystem,
        };

        await _messagingService.SubmitPassing(passing);
    }
}
