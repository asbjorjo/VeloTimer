using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.Timing.Contracts;
using VeloTime.Timing.Handlers;
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
                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Publish<TrackPassing>(x =>
                    {
                        x.Durable = true;
                        x.AutoDelete = false;
                    });
                    cfg.ReceiveEndpoint(nameof(TrackPassing),x =>
                    {
                        x.Bind<TrackPassing>();

                        x.ConfigureConsumeTopology = false;
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
