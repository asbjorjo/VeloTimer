using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace VeloTimer.PassingLoader.Services.Messaging
{
    public static class Startup
    {
        public static IServiceCollection ConfigureMessaging(this IServiceCollection services, IConfiguration configuration)
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
                x.UsingRabbitMq();
            });

            var mbconfig = configuration.GetSection(MessageBusOptions.Section);
            var settings = mbconfig.Get<MessageBusOptions>() ?? new MessageBusOptions();
            settings.ConnectionString = configuration.GetConnectionString(MessageBusOptions.ConnectionStringProperty);

            services.TryAddSingleton(settings);
            services.TryAddSingleton<IMessagingService, LogMessagingService>();

            return services;
        }
    }
}
