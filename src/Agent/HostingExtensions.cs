using Azure.Messaging.ServiceBus.Administration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
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


        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(_env.ApplicationName, "VeloTime.Agent", "2.0.0"))
            .WithTracing(tracing => tracing
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddSource("Azure.Messaging.ServiceBus")
                .AddSource("Azure.Messaging.ServiceBus.*")
                .AddSource("VeloTime.Agent")
            )
            .WithMetrics(metrics => metrics
                .AddHttpClientInstrumentation()
                .AddMeter("VeloTime.Agent")
            )
            .UseOtlpExporter();

        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
        });

        //services.AddSerilog(lc => lc
        //    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}")
        //    .WriteTo.OpenTelemetry(options =>
        //    {
        //        options.ResourceAttributes = new Dictionary<string, object>
        //        {
        //            ["service.name"] = _env.ApplicationName,
        //        };
        //    })
        //    .Enrich.FromLogContext()
        //    .ReadFrom.Configuration(configuration)
        //    );

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
                    Enabled = false,
                    CanConsumerCreateQueue = false,
                    CanConsumerCreateTopic = false,
                    CanConsumerCreateSubscription = true,
                    CanConsumerCreateSubscriptionFilter = true,
                    CanConsumerValidateSubscriptionFilters = true,
                    CanProducerCreateQueue = false,
                    CanProducerCreateTopic = false,
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
                configuration.GetConnectionString("AgentDbConnection"),
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
