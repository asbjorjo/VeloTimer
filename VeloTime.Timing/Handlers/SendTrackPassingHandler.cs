using MediatR;
using VeloTime.Timing.Commands;
using VeloTime.Timing.Services;

namespace VeloTime.Timing.Handlers
{
    public class SendTrackPassingHandler : IRequestHandler<SendTrackPassing>
    {
        private readonly IMessagingService _messagingService;

        public SendTrackPassingHandler(IMessagingService messagingService) => _messagingService = messagingService;

        public async Task Handle(SendTrackPassing request, CancellationToken cancellationToken)
        {
            await _messagingService.SendTrackPassing(request.TrackPassing);
        }
    }
}
