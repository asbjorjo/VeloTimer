using MediatR;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Timing.Agents.PassingObserver.Commands;
using VeloTime.Timing.Handlers;

namespace VeloTime.Timing.Agents.PassingObserver.Handlers;

public static class Extensions
{
    public static IServiceCollection AddSendTrackPassingHandler(this IServiceCollection services)
    {
        return services
            .AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(typeof(IMediator).Assembly))
            .AddTransient<IRequestHandler<SendTrackPassing>, SendTrackPassingHandler>();
    }
}
