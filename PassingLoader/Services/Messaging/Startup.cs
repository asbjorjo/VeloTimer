﻿using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VeloTimer.PassingLoader.Consumers;

namespace VeloTimer.PassingLoader.Services.Messaging
{
    public static class Startup
    {
        public static IServiceCollection AddExternalMessagingervice(this IServiceCollection services, IConfiguration configuration)
        {
            var mbconfig = configuration.GetSection(MessageBusOptions.Section);
            var settings = mbconfig.Get<MessageBusOptions>() ?? new MessageBusOptions();
            settings.ConnectionString = configuration.GetConnectionString(MessageBusOptions.ConnectionStringProperty);

            services.TryAddSingleton(settings);
            services.TryAddSingleton<IExternalMessagingService, LogMessagingService>();

            return services;
        }

        public static IServiceCollection AddMessagingService(this IServiceCollection services, IConfiguration config)
        {
            services.AddOptions<RabbitMqTransportOptions>().Configure(options =>
            {
                options.Host = "rabbitmq";
                options.User = "guest";
                options.Pass = "guest";
            });
            services.AddMassTransit(x =>
            {
                x.AddConsumer<PassingObservedConsumer>();
                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.ConfigureEndpoints(context);
                });
            });

            return services;
        }
    }
}