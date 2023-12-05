using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace VeloTime.Timing.Agents.EventSubmitter.Services;

public static class Extensions
{
    public static IHostBuilder AddServiceBusMessaging(this IHostBuilder hostBuilder)
    {
        hostBuilder.ConfigureServices((context, services) =>
        {
            var configuration = context.Configuration;
            var mbconfig = configuration.GetSection(MessageBusOptions.Section);
            var settings = mbconfig.Get<MessageBusOptions>() ?? new MessageBusOptions();
            settings.ConnectionString = configuration.GetConnectionString(MessageBusOptions.ConnectionStringProperty);

            services.TryAddSingleton(settings);
            services.TryAddSingleton<IExternalMessagingService, ServiceBusMessagingService>();

        });

        return hostBuilder;
    }

    public static IHostBuilder AddLoggingMessaging(this IHostBuilder hostBuilder)
    {
        hostBuilder.ConfigureServices(services => 
        {
            services.TryAddSingleton<IExternalMessagingService, LogMessagingService>(); 
        });

        return hostBuilder;
    }
}
