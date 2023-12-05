using MassTransit;
using Microsoft.Extensions.Hosting;
using VeloTime.Timing.Agents.EventSubmitter.Consumers;

namespace VeloTime.Timing.Agents.EventSubmitter;

public static class Extensions
{
    public static IHostBuilder AddMassTransit(this IHostBuilder hostBuilder)
    {
        hostBuilder
            .ConfigureServices((context, services) =>
             {
                 services.AddMassTransit(x =>
                 {
                     x.AddConsumer<TrackPassingConsumer>();
                     x.UsingRabbitMq((context, cfg) =>
                     {
                         cfg.ConfigureEndpoints(context);
                     });
                 });
             });

        return hostBuilder;
    }
}
