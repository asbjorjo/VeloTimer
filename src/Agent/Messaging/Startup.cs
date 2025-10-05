using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Agent.Messaging;

public static class Startup
{
    public static IServiceCollection ConfigureMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var mbconfig = configuration.GetSection(MessageBusOptions.Section);
        var settings = mbconfig.Get<MessageBusOptions>() ?? new MessageBusOptions();
        settings.ConnectionString = configuration.GetConnectionString(MessageBusOptions.ConnectionStringProperty) ?? string.Empty;

        services.AddSlimMessageBus(mbb =>
        {
            mbb.WithProviderServiceBus(config =>
            {
                config.ConnectionString = settings.ConnectionString;
                config.TopologyProvisioning = new ServiceBusTopologySettings
                {
                    Enabled = false
                };
            });
            mbb.Produce<PassingObserved>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = message.TransponderType + message.TransponderId;
                }));
            mbb.AddJsonSerializer();
        });

        return services;
    }
}
