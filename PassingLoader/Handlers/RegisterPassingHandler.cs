using MediatR;
using VeloTimer.PassingLoader.Commands;
using VeloTimer.PassingLoader.Services.Messaging;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.PassingLoader.Handlers
{
    public class RegisterPassingHandler : IRequestHandler<RegisterPassingCommand>
    {
        private readonly IExternalMessagingService _messagingService;

        public RegisterPassingHandler(IExternalMessagingService messagingService) => _messagingService = messagingService;

        public async Task Handle(RegisterPassingCommand request, CancellationToken cancellationToken)
        {
            PassingRegister passing = new PassingRegister
            {
                Time = request.Passing.Time,
                TransponderId = request.Passing.Transponder,
                LoopId = request.Passing.PassingPoint,
                Source = request.Passing.Source,
                TimingSystem = request.Passing.TimingSystem,
                Track = "test"
            };

            await _messagingService.SubmitPassing(passing);
        }
    }
}
