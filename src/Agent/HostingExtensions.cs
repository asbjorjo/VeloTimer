using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SlimMessageBus;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Outbox.PostgreSql.DbContext;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Agent.Capabilities;
using VeloTime.Agent.Handler;
using VeloTime.Agent.Interface;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Agent.Messaging;
using VeloTime.Agent.Service;
using VeloTime.Agent.Storage;

namespace VeloTime.Agent;

public static class StartupExtensions
{
    private static IHostEnvironment? _env;

    /// <summary>
    /// Registers the shared agent infrastructure. <paramref name="agentType"/> is the constant the agent passes to
    /// <see cref="Identity.PassingId"/> (e.g. <c>P3</c>, <c>X2</c>); it is reported in <c>AgentCapabilitiesV1</c>.
    /// When omitted, <c>Agent:AgentType</c> from configuration is used, else <c>Unknown</c>.
    /// </summary>
    public static IHostApplicationBuilder ConfigureAgent(this IHostApplicationBuilder builder, string? agentType = null)
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

        services.AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.Section))
            .Configure(options =>
            {
                options.AgentId = agentId;
                if (!string.IsNullOrWhiteSpace(agentType)) options.AgentType = agentType;
            });

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
            });
            mbb.WithHeaderModifier((headers, message) =>
            {
                headers[AgentControl.AgentIdHeader] = agentId;
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
            mbb.Produce<PassingObservedV1>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = $"{message.TransponderType}_{message.TransponderId}";
                    sbMessage.MessageId = $"{agentId}:{message.SourcePassingId}";
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
            mbb.Produce<AgentCapabilitiesV1>(x => x
                .DefaultTopic(settings.TopicName)
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = agentId;
                }));
            // Commands arrive on the control topic. The agent provisions its own subscription, named after the agent,
            // with a correlation filter on the AgentId application property, so onboarding an agent needs no deploy.
            ConsumeControl<PauseAgentCommand, PauseAgentHandler>(mbb, settings.ControlTopicName, agentId);
            ConsumeControl<ResumeAgentCommand, ResumeAgentHandler>(mbb, settings.ControlTopicName, agentId);
            ConsumeControl<ReportCapabilitiesCommandV1, ReportCapabilitiesHandler>(mbb, settings.ControlTopicName, agentId);
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
        services.AddSingleton<IAgentCapabilityProvider, PauseResumeCapabilityProvider>();
        services.AddSingleton<CapabilityReporter>();
        services.AddHostedService(provider => provider.GetRequiredService<CapabilityReporter>());

        //services.ConfigureApiClient(configuration);

        return builder;
    }

    private static void ConsumeControl<TCommand, THandler>(MessageBusBuilder mbb, string topic, string agentId)
        where THandler : class, IConsumer<TCommand>
    {
        mbb.Consume<TCommand>(x => x
            .Topic(topic)
            .WithConsumer<THandler>()
            .SubscriptionName(agentId)
            .SubscriptionCorrelationFilter(applicationProperties: new Dictionary<string, object>
            {
                [AgentControl.AgentIdHeader] = agentId
            })
            .Instances(1));
    }

    public static async Task RunAgent(this HostApplicationBuilder builder)
    {
        var host = builder.Build();

        host.ApplyMigrations();

        await host.RunAsync();
    }
}
