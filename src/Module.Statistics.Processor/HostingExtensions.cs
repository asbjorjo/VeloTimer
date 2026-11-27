using Duende.AccessTokenManagement;
using OpenTelemetry.Trace;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Common.Operations;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Timing.Interface.Messages;

internal static class StartupExtensions
{
    internal static IHostApplicationBuilder AddServices(this IHostApplicationBuilder builder)
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
            // Passing facts, ownership changes and installation facility changes share the session-enabled "statistics" subscription on the
            // Timing topic; passings and ownership are keyed by transponder, facility changes by installation. Statistics no longer messages itself (plan §3.3).
            mbb.Consume<PassingRecordedV1>(x => x
                .Topic("velotime-timing")
                .SubscriptionName("statistics")
                .SubscriptionSqlFilter($"MessageName = '{nameof(PassingRecordedV1)}'")
                .WithConsumer<PassingRecordedHandler>()
                .Instances(1)
                .EnableSession());
            mbb.Consume<TransponderOwnershipChanged>(x => x
                .Topic("velotime-timing")
                .SubscriptionName("statistics")
                .SubscriptionSqlFilter($"MessageName = '{nameof(TransponderOwnershipChanged)}'")
                .WithConsumer<TransponderOwnershipChangedHandler>()
                .Instances(1)
                .EnableSession());
            // Facilities course configuration changes, one session per facility so a facility's edits stay in order.
            mbb.Consume<CourseConfigChangedV1>(x => x
                .Topic("velotime-facilities")
                .SubscriptionName("statistics")
                .SubscriptionSqlFilter($"MessageName = '{nameof(CourseConfigChangedV1)}'")
                .WithConsumer<CourseConfigChangedHandler>()
                .Instances(1)
                .EnableSession());
            mbb.Consume<InstallationFacilityChangedV1>(x => x
                .Topic("velotime-timing")
                .SubscriptionName("statistics")
                .SubscriptionSqlFilter($"MessageName = '{nameof(InstallationFacilityChangedV1)}'")
                .WithConsumer<InstallationFacilityChangedHandler>()
                .Instances(1)
                .EnableSession());
            mbb.AddServicesFromAssemblyContaining<TransponderOwnershipChangedHandler>();
        });

        builder.AddModuleIdentity("velotime.statistics.processor");

        services.AddTimingClient()
            .AddDefaultAccessTokenResiliency()
            .AddClientCredentialsTokenHandler(ClientCredentialsClientName.Parse("velotime.statistics.processor"));
        services.AddScoped<ITransponderOwnershipReader, TransponderOwnershipReader>();
        services.AddScoped<StatisticsOwnershipService>();
        services.AddOptions<StatisticsRecomputeOptions>()
            .Bind(configuration.GetSection(StatisticsRecomputeOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<StatisticsRecomputeSignal>();
        services.AddScoped<IStatisticsConfigurationSource, HttpStatisticsConfigurationSource>();
        services.AddScoped<StatisticsConfigurationHistory>();
        services.AddScoped<StatisticsRecomputeService>();
        services.AddHostedService<StatisticsRecomputeWorker>();
        services.AddOptions<StatisticsBacklogMonitorOptions>()
            .Bind(configuration.GetSection(StatisticsBacklogMonitorOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHostedService<StatisticsBacklogMonitor>();

        services.AddFacilitiesClient()
            .AddDefaultAccessTokenResiliency()
            .AddClientCredentialsTokenHandler(ClientCredentialsClientName.Parse("velotime.statistics.processor"));

        builder.AddModuleStatistics();
        builder.AddDeadLetterMonitor(
            new DeadLetterSubscription("velotime-timing", "statistics"),
            new DeadLetterSubscription("velotime-facilities", "statistics"));

        return builder;
    }
}
