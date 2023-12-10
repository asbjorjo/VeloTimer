using MassTransit;
using MassTransit.Internals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.Timing.Agents.PassingObserver.Consumers;
using VeloTime.Timing.Agents.PassingObserver.Handlers;
using VeloTime.Timing.Contracts;
using VeloTime.Timing.Services;
using VeloTime.Timing.Services.Impl;

namespace VeloTime.Timing.Agents.PassingObserver;

public static class Extensions
{
    internal static IHostBuilder AddMessagingService(this IHostBuilder hostBuilder)
    {
        hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddOptions<MassTransitHostOptions>().Configure(options =>
            {
                options.WaitUntilStarted = true;
            });
            services.AddSingleton<IMessagingService, MassTransitMessagingService>();
            services.AddOptions<RabbitMqTransportOptions>().Configure(options =>
            {
                options.Host = "rabbitmq";
                options.User = "guest";
                options.Pass = "guest";
            });
            services.AddMassTransit(x =>
            {
                x.AddConsumer<StartLoadingFromConsumer>();

                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Publish<TrackPassing>(x =>
                    {
                        x.Durable = true;
                        x.AutoDelete = false;
                    });

                    cfg.ConfigureEndpoints(context);
                });
            });
        });


        return hostBuilder;
    }

    public static IHostBuilder UsePassingObserver(this IHostBuilder hostBuilder)
    {
        hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddSendTrackPassingHandler();
        });

        hostBuilder.AddMessagingService();

        return hostBuilder;
    }
}
