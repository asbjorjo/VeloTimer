using OpenTelemetry.Trace;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api;

public static class MessagingExtensions
{
    public const string ValidityThresholdsTopic = "velotime-statistics-test";

    /// <summary>
    /// Registers transport and transactional outbox persistence only. The API's
    /// enqueue abstraction must require the same scoped StatisticsDbContext and
    /// active PostgreSQL transaction used to save definitions and schedule work.
    /// </summary>
    public static IHostApplicationBuilder AddStatisticsValidityThresholdsMessaging(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("MessageBus");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:MessageBus is required for Statistics validity-threshold events.");

        builder.Services.ConfigureOpenTelemetryTracerProvider(tracer => tracer.AddSource("Azure.Messaging.*"));
        builder.Services.AddScoped<IStatisticsThresholdEnqueue, StatisticsThresholdEnqueue>();
        builder.Services.AddSlimMessageBus(mbb =>
        {
            mbb.AutoStartConsumersEnabled(false);
            mbb.WithProviderServiceBus(options =>
            {
                options.ConnectionString = connectionString;
                // External topology must exist before rollout. Broker outages must
                // not prevent definition/outbox commits or trigger a direct-send fallback.
                options.TopologyProvisioning.Enabled = false;
            });
            mbb.AddJsonSerializer();
            mbb.WithHeaderModifier((headers, message) => headers["MessageName"] = message.GetType().Name);
            mbb.Produce<StatisticsValidityThresholdsChangedV1>(producer => producer
                .DefaultTopic(ValidityThresholdsTopic)
                .WithModifier((message, transportMessage) =>
                {
                    transportMessage.MessageId = message.EventId.ToString();
                    // Item-wide and configuration edits share one ordering scope.
                    transportMessage.SessionId = message.StatisticsItemId.ToString();
                }));
            mbb.AddStatisticsOutbox();
        });
        builder.Services.AddHostedService<StatisticsValidityThresholdsOutboxStartup>();
        return builder;
    }
}

internal sealed class StatisticsValidityThresholdsOutboxStartup(IMasterMessageBus bus) : IHostedService
{
    // Explicit Start propagates database/outbox initialization errors to startup.
    public Task StartAsync(CancellationToken cancellationToken) => bus.Start();
    public Task StopAsync(CancellationToken cancellationToken) => bus.Stop();
}
