using Duende.AccessTokenManagement;
using OpenTelemetry.Trace;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
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
            // Passing facts and ownership changes share the session-enabled "statistics" subscription on the
            // Timing topic; both are keyed by transponder. Statistics no longer messages itself (plan §3.3).
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
            // Wake-up only: durable job rows are the source of work.
            mbb.Consume<StatisticsValidityThresholdsChangedV1>(x => x
                .Topic("velotime-statistics")
                .SubscriptionName("statistics-validity")
                .SubscriptionSqlFilter($"MessageName = '{nameof(StatisticsValidityThresholdsChangedV1)}'")
                .WithConsumer<StatisticsValidityThresholdsChangedHandler>()
                .Instances(1));
            mbb.AddServicesFromAssemblyContaining<TransponderOwnershipChangedHandler>();
        });

        builder.AddModuleIdentity("velotime.statistics.processor");

        services.AddTimingClient()
            .AddDefaultAccessTokenResiliency()
            .AddClientCredentialsTokenHandler(ClientCredentialsClientName.Parse("velotime.statistics.processor"));
        services.AddScoped<ITransponderOwnershipReader, TransponderOwnershipReader>();
        services.AddScoped<StatisticsOwnershipService>();
        services.AddOptions<StatisticsRevalidationOptions>()
            .Bind(configuration.GetSection(StatisticsRevalidationOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<StatisticsRevalidationSignal>();
        services.AddScoped<StatisticsRevalidationService>();
        services.AddHostedService<StatisticsRevalidationWorker>();
        services.AddOptions<StatisticsRecomputeOptions>()
            .Bind(configuration.GetSection(StatisticsRecomputeOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<StatisticsRecomputeSignal>();
        services.AddScoped<IStatisticsConfigurationSource, HttpStatisticsConfigurationSource>();
        services.AddScoped<StatisticsConfigurationHistory>();
        services.AddScoped<StatisticsRecomputeService>();
        services.AddHostedService<StatisticsRecomputeWorker>();

        services.AddFacilitiesClient()
            .AddDefaultAccessTokenResiliency()
            .AddClientCredentialsTokenHandler(ClientCredentialsClientName.Parse("velotime.statistics.processor"));

        builder.AddModuleStatistics();

        return builder;
    }
}
