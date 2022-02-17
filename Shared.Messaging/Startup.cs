using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace VeloTime.Shared.Messaging
{
    public static class Startup
    {
        public static IServiceCollection ConfigureMessaging(this IServiceCollection services, IConfiguration configuration)
        {
            var mbconfig = configuration.GetSection(MessageBusOptions.Section);
            var settings = mbconfig.Get<MessageBusOptions>() ?? new MessageBusOptions();
            settings.ConnectionString = configuration.GetConnectionString(MessageBusOptions.ConnectionStringProperty);

            services.TryAddSingleton(settings);

            return services;
        }

        public static IServiceCollection AddMessaging(this IServiceCollection services)
        {
            services.TryAddSingleton<IMessagingService, MessagingService>();
            return services;
        }
    }
}
