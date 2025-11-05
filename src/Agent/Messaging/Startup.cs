using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Agent.Interface.Messages;

namespace VeloTime.Agent.Messaging;

public static class Startup
{
    public static IServiceCollection ConfigureMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        string agentId = configuration.GetValue("VELOTIME_AGENT", string.Empty);

        if (agentId == string.Empty)
        {
            throw new ArgumentException("VELOTIME_AGENT environment variable is not set");
        }

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
                config.WithModifier((message, sbMessage) =>
                {
                    sbMessage.Subject = agentId;
                });
            });
            mbb.Produce<PassingObserved>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = message.TransponderType + message.TransponderId;
                }));
            mbb.Produce<TimingLoopStatus>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = agentId;
                }));
            mbb.Produce<SystemConfig>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = agentId;
                }));
            mbb.Produce<SegmentConfig>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = agentId;
                }));
            mbb.Produce<TimingLoopConfig>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = agentId;
                }));
            mbb.AddJsonSerializer();
        });

        return services;
    }
}
