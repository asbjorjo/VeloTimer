using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.Timing.Agents.EventSubmitter.Consumers;
using VeloTime.Timing.Agents.EventSubmitter.Services;
using VeloTime.Timing.Contracts;

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
                         cfg.Send<StartLoadingFrom>();
                         cfg.ConfigureEndpoints(context);
                     });
                 });
             });

        return hostBuilder;
    }

    public static IHostBuilder AddStartupService(this IHostBuilder hostBuilder)
    {
        hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddHostedService<StartupService>();
        });
        return hostBuilder;
    }
}
