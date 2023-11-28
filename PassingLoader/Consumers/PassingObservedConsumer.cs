using MassTransit;
using MediatR;
using VeloTimer.PassingLoader.Commands;
using VeloTimer.PassingLoader.Contracts;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.PassingLoader.Consumers
{
    public class PassingObservedConsumer : IConsumer<RawPassingObserved>
    {
        private readonly IMediator _mediator;

        public PassingObservedConsumer(IMediator mediator)
        {
            _mediator = mediator;
        }

        public Task Consume(ConsumeContext<RawPassingObserved> context)
        {
            RawPassingObserved message = context.Message;

            return _mediator.Send(new RegisterPassingCommand(message));
        }
    }
}
