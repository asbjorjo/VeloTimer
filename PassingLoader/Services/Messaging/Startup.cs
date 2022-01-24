using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace VeloTimer.PassingLoader.Services.Messaging
{
    public static class Startup
    {
        public static IServiceCollection ConfigureMessaging(this IServiceCollection services, IConfiguration configuration)
        {
            var mbconfig = configuration.GetSection(MessageBusOptions.Section);
            var settings = mbconfig.Get<MessageBusOptions>() ?? new MessageBusOptions();
            settings.ConnectionString = configuration.GetConnectionString(MessageBusOptions.ConnectionStringProperty);

            services.TryAddSingleton(settings);
            services.TryAddSingleton<IMessagingService, MessagingService>();

            return services;
        }
    }
}
