using MassTransit;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.Timing.Services.Impl;

namespace VeloTime.Timing.Services;

public static class Extensions
{
    public static IHostBuilder AddMasstransitMessaging(this IHostBuilder hostBuilder)
    {
        hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddOptions<MassTransitHostOptions>().Configure(options =>
            {
                options.WaitUntilStarted = true;
            });

            services.AddOptions<RabbitMqTransportOptions>().Configure(options =>
            {
                options.Host = "rabbitmq";
                options.User = "guest";
                options.Pass = "guest";
            });

            services.TryAddSingleton<IMessagingService, MassTransitMessagingService>();

        });

        return hostBuilder;
    }
}
