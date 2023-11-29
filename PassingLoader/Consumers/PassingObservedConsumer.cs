using MassTransit;
using MediatR;
using VeloTimer.PassingLoader.Commands;
using VeloTimer.PassingLoader.Contracts;

namespace VeloTimer.PassingLoader.Consumers
{
    public class PassingObservedConsumer : IConsumer<TrackPassingObserved>
    {
        private readonly IMediator _mediator;

        public PassingObservedConsumer(IMediator mediator)
        {
            _mediator = mediator;
        }

        public Task Consume(ConsumeContext<TrackPassingObserved> context)
        {
            TrackPassingObserved message = context.Message;

            return _mediator.Send(new RegisterTrackPassing(message));
        }
    }
}
