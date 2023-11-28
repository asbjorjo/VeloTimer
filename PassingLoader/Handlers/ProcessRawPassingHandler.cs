using MediatR;
using VeloTimer.PassingLoader.Commands;
using VeloTimer.PassingLoader.Contracts;
using VeloTimer.PassingLoader.Services.Messaging;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.PassingLoader.Handlers
{
    public class ProcessRawPassingHandler : IRequestHandler<ProcessRawPassing>
    {
        private readonly IMediator _mediator;

        public ProcessRawPassingHandler(IMediator mediator) => _mediator = mediator;

        public async Task Handle(ProcessRawPassing request, CancellationToken cancellationToken)
        {
            TrackPassingObserved trackPassingObserved = new TrackPassingObserved
            {
                Track = "test",
                Time = request.Passing.Time,
                Transponder = request.Passing.Transponder,
                PassingPoint = request.Passing.PassingPoint,
                Source = request.Passing.Source,
                TimingSystem = request.Passing.TimingSystem,
                LowBattery = request.Passing.LowBattery,
                LowSignal = request.Passing.LowSignal
            };

            await _mediator.Send(new RegisterTrackPassing(trackPassingObserved));
        }
    }
}
