using MediatR;
using VeloTimer.PassingLoader.Services.Messaging;

namespace VeloTime.X2.Core.Handlers
{
    public class RegisterPassingHandler : IRequestHandler<RegisterPassingCommand>
    {
        private readonly IMessagingService _messagingService;

        public RegisterPassingHandler(IMessagingService messagingService) => _messagingService = messagingService;

        public async Task Handle(RegisterPassingCommand request, CancellationToken cancellationToken)
        {
            await _messagingService.SubmitPassings(request.Passings);
        }
    }
}
