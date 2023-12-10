using MassTransit;
using MediatR;
using VeloTime.Timing.Contracts;

namespace VeloTime.Timing.Agents.PassingObserver.Consumers;

public class StartLoadingFromConsumer : IConsumer<StartLoadingFrom>
{
    private readonly IMediator _mediator;

    public StartLoadingFromConsumer(IMediator mediator) => _mediator = mediator;

    public async Task Consume(ConsumeContext<StartLoadingFrom> context)
    {
        await _mediator.Publish(context.Message.StartTime);
    }
}
