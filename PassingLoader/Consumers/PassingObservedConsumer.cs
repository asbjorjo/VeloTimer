using MassTransit;
using MassTransit.Mediator;
using System.Net.Http.Headers;
using VeloTimer.Shared.Data.Models.Timing;

public class PassingObservedConsumer : IConsumer<PassingObserved>
{
    private readonly IMediator _mediator;

    public PassingObservedConsumer(IMediator mediator)
    {
        _mediator = mediator;
    }

    public Task Consume(ConsumeContext<PassingObserved> context)
    {
        PassingObserved message = context.Message;
        PassingRegister passing = new PassingRegister
        {
            LoopId = message.PassingPoint,
            Time = message.Time,
            TimingSystem = message.TimingSystem,
            Source = message.Source,
            TransponderId = message.Transponder,
            Track = ""
        };

        return _mediator.Send(new RegisterPassingCommand(passing));
    }
}