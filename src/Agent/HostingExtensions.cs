using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Outbox.PostgreSql.DbContext;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Agent.Handler;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Agent.Messaging;
using VeloTime.Agent.Service;
using VeloTime.Agent.Storage;
//using VeloTimer.PassingLoader.Services.Api;

namespace VeloTime.Agent;

public static class StartupExtensions
{
    private static IHostEnvironment? _env;
    public static IHostApplicationBuilder ConfigureAgent(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        configuration.AddEnvironmentVariables("VELOTIME_");

        _env = builder.Environment;

        builder.AddServiceDefaults();

        services.ConfigureOpenTelemetryTracerProvider(tracer =>
        {
            tracer.AddSource("Azure.Messaging.*");
            tracer.AddSource("VeloTime.Agent");
        });
        services.ConfigureOpenTelemetryMeterProvider(metrics =>
        {
            metrics.AddMeter("VeloTime.Agent");
        });

        string agentId = configuration.GetValue("VELOTIME_AGENT", string.Empty);

        if (agentId == string.Empty)
        {
            throw new ArgumentException("VELOTIME_AGENT environment variable is not set");
        }

        var mbconfig = configuration.GetSection(MessageBusOptions.Section);
        var settings = mbconfig.Get<MessageBusOptions>() ?? new MessageBusOptions();
        if (settings.OutboxPollIdleSleep <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(settings.OutboxPollIdleSleep));
        settings.ConnectionString = configuration.GetConnectionString(MessageBusOptions.ConnectionStringProperty) ?? string.Empty;

        services.AddSlimMessageBus(mbb =>
        {
            mbb.WithProviderServiceBus(config =>
            {

                config.ConnectionString = settings.ConnectionString;
                config.ClientFactory = (_, provider) => new ServiceBusClient(provider.ConnectionString, new ServiceBusClientOptions
                {
                    RetryOptions = new ServiceBusRetryOptions
                    {
                        Mode = ServiceBusRetryMode.Exponential,
                        MaxRetries = 2,
                        Delay = TimeSpan.FromSeconds(1),
                        MaxDelay = TimeSpan.FromSeconds(5),
                        TryTimeout = TimeSpan.FromSeconds(10)
                    }
                });
                config.TopologyProvisioning = new ServiceBusTopologySettings
                {
                    Enabled = true,
                    CanConsumerCreateQueue = true,
                    CanConsumerCreateTopic = true,
                    CanConsumerCreateSubscription = true,
                    CanConsumerCreateSubscriptionFilter = true,
                    CanConsumerValidateSubscriptionFilters = true,
                    CanProducerCreateQueue = true,
                    CanProducerCreateTopic = true,
                };
            });
            mbb.WithHeaderModifier((headers, message) =>
            {
                headers["AgentId"] = agentId;
                headers["MessageName"] = message.GetType().Name;
            });
            mbb.Produce<InstallationLayoutEvent>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = agentId;
                }));
            mbb.Produce<LoopConfigEvent>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = agentId;
                }));
            mbb.Produce<LoopStatusEvent>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = agentId;
                }));
            mbb.Produce<PassingEvent>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = $"{message.TransponderType}_{message.TransponderId}";
                }));
            mbb.Produce<SegmentConfigEvent>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = agentId;
                }));
            mbb.Produce<SystemConfigEvent>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = agentId;
                }));
            mbb.Consume<PauseAgentCommand>(x => x
                .Topic(settings.TopicName)
                .WithConsumer<PauseAgentHandler>()
                .SubscriptionName(agentId)
                .SubscriptionCorrelationFilter(applicationProperties: new Dictionary<string, object> { { "agentId", agentId } })
                .Instances(1));
            mbb.Consume<ResumeAgentCommand>(x => x
                .Topic(settings.TopicName)
                .WithConsumer<ResumeAgentHandler>()
                .SubscriptionName(agentId)
                .SubscriptionCorrelationFilter(applicationProperties: new Dictionary<string, object> { { "agentId", agentId } })
                .Instances(1));
            mbb.AddServicesFromAssemblyContaining<PauseAgentHandler>();
            mbb.AddJsonSerializer();
            mbb.UseOutbox();
            mbb.AddOutboxUsingDbContext<AgentDbContext>(options =>
            {
                options.PostgreSqlSettings.DatabaseSchemaName = "agent";
                options.MaintainSequence = true;
                options.MaxDeliveryAttempts = int.MaxValue;
                options.MessageTypeResolver = new OutboxMessageTypeResolver(options.MessageTypeResolver);
                options.PollIdleSleep = settings.OutboxPollIdleSleep;
                // A failed oldest message must not let later messages in the same batch advance.
                options.PollBatchSize = 1;
                options.MessageCleanup = new OutboxMessageCleanupSettings
                {
                    Enabled = true,
                    BatchSize = 100,
                    Age = TimeSpan.FromDays(3),
                    Interval = TimeSpan.FromHours(1)
                };
            });
        });

        services.AddDbContextFactory<AgentDbContext>(options =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("AgentDb"),
                x => { x.MigrationsHistoryTable("__Migrations", "agent"); }
                );
            options.UseSnakeCaseNamingConvention();
        });

        services.AddScoped<MessagingService>();
        services.AddSingleton<Metrics>();

        //services.ConfigureApiClient(configuration);

        return builder;
    }

    public static async Task RunAgent(this HostApplicationBuilder builder)
    {
        var host = builder.Build();

        host.ApplyMigrations();

        await host.RunAsync();
    }
}
