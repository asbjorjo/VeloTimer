using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Timing;
using VeloTime.Module.Timing.Handlers;
using VeloTime.Module.Timing.Messaging;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;
using VeloTime.Module.Common.Operations;

public static class StartupExtensions
{
    public static IHostApplicationBuilder AddServices(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        services.ConfigureOpenTelemetryTracerProvider(tracer =>
        {
            tracer.AddSource("Azure.Messaging.*");
        });

        services.AddSlimMessageBus(mbb =>
        {
            mbb.WithProviderServiceBus(options =>
            {
                options.ConnectionString = configuration.GetConnectionString("MessageBus");
                options.MaxConcurrentSessions = 10;
                options.SessionIdleTimeout = TimeSpan.FromSeconds(5);
                // Topics, subscriptions and filter rules are owned by the VeloTime.MessageBus.Topology
                // deployment job so its declared rules stay authoritative.
                options.TopologyProvisioning.Enabled = false;
            });
            mbb.AddJsonSerializer();
            mbb.WithHeaderModifier((headers, message) =>
            {
                headers["MessageName"] = message.GetType().Name;
            });
            mbb.Consume<PassingObservedV1>(x => x
                .Topic("velotime-agent")
                .SubscriptionName("timing")
                .SubscriptionSqlFilter($"MessageName = '{nameof(PassingObservedV1)}'")
                .WithConsumer<PassingObservedHandler>()
                .Instances(1)
                .EnableSession(s =>
                {
                    //s.MaxConcurrentSessions(1);
                }));
            // PassingRecordedV1 is enqueued in the passing's transaction and dispatched from timing.smb_outbox.
            mbb.AddTimingEventsWithOutbox();
            mbb.Consume<InstallationLayoutEvent>(x => x
                .Topic("velotime-agent")
                .SubscriptionName("infrastructure")
                .SubscriptionSqlFilter($"MessageName = '{nameof(InstallationLayoutEvent)}'")
                .WithConsumer<InstallationLayoutHandler>()
                .Instances(1));
            mbb.Consume<LoopStatusEvent>(x => x
                .Topic("velotime-agent")
                .SubscriptionName("infrastructure")
                .SubscriptionSqlFilter($"MessageName = '{nameof(LoopStatusEvent)}'")
                .WithConsumer<LoopStatusEventHandler>());
            mbb.Consume<AgentCapabilitiesV1>(x => x
                .Topic("velotime-agent")
                .SubscriptionName("infrastructure")
                .SubscriptionSqlFilter($"MessageName = '{nameof(AgentCapabilitiesV1)}'")
                .WithConsumer<AgentCapabilitiesHandler>());
            mbb.AddServicesFromAssemblyContaining<PassingObservedHandler>();
        });

        services.AddScoped<IAgentService, AgentService>();

        builder.AddModuleTiming();
        // Delivery health (plan Phase 5): the shared Timing outbox and this host's dead-letter queues.
        builder.AddOutboxMonitor<TimingDbContext>("timing", "timing");
        builder.AddDeadLetterMonitor(
            new DeadLetterSubscription("velotime-agent", "timing"),
            new DeadLetterSubscription("velotime-agent", "infrastructure"));

        return builder;
    }
}
