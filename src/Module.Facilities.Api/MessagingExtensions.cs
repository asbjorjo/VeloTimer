using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Outbox.PostgreSql.DbContext;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api;

public static class MessagingExtensions
{
    public const string TimingAssociationsTopic = "velotime-facilities";

    public static IHostApplicationBuilder AddTimingAssociationMessaging(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("MessageBus");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:MessageBus is required for Facilities timing-association events.");

        builder.Services.ConfigureOpenTelemetryTracerProvider(tracer => tracer.AddSource("Azure.Messaging.*"));
        builder.Services.AddScoped<ITimingAssociationEventPublisher, TimingAssociationEventPublisher>();
        builder.Services.AddSlimMessageBus(mbb =>
        {
            mbb.AutoStartConsumersEnabled(false);
            mbb.WithProviderServiceBus(options =>
            {
                options.ConnectionString = connectionString;
                // Facilities topology must be precreated externally; never reuse Timing's topic.
                options.TopologyProvisioning.Enabled = true;
            });
            mbb.AddJsonSerializer();
            mbb.WithHeaderModifier((headers, message) => headers["MessageName"] = message.GetType().Name);
            mbb.Produce<CoursePointTimingAssociationsChangedV1>(producer => producer
                .DefaultTopic(TimingAssociationsTopic)
                .WithModifier((message, transportMessage) =>
                {
                    // Atomic changes may span several coursepoints in one facility.
                    transportMessage.SessionId = message.FacilityId.ToString();
                    transportMessage.MessageId = message.EventId.ToString();
                }));
            mbb.UseOutbox();
            mbb.AddOutboxUsingDbContext<FacilityDbContext>(options =>
            {
                options.PostgreSqlSettings.DatabaseSchemaName = "facility";
                options.MaintainSequence = true;
                options.PollBatchSize = 1;
                options.PollIdleSleep = TimeSpan.FromSeconds(5);
                options.MaxDeliveryAttempts = int.MaxValue;
                // Dispatched payloads are the explicit DB replay source until durable
                // consumer topology and an approved archival/replay policy exist.
                options.MessageCleanup = new OutboxMessageCleanupSettings { Enabled = false };
            });
        });
        builder.Services.AddHostedService<TimingAssociationOutboxStartup>();
        return builder;
    }
}

internal sealed class TimingAssociationOutboxStartup(IMasterMessageBus bus) : IHostedService
{
    // Explicit Start propagates outbox schema initialization failures to host startup.
    public Task StartAsync(CancellationToken cancellationToken) => bus.Start();
    public Task StopAsync(CancellationToken cancellationToken) => bus.Stop();
}
