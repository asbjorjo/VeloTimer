using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using SlimMessageBus.Host;
using VeloTime.Module.Common.Operations;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Outbox.PostgreSql.DbContext;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api;

public static class MessagingExtensions
{
    public const string CourseConfigTopic = "velotime-facilities";

    public static IHostApplicationBuilder AddCourseConfigMessaging(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("MessageBus");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:MessageBus is required for Facilities course configuration events.");

        builder.Services.ConfigureOpenTelemetryTracerProvider(tracer => tracer.AddSource("Azure.Messaging.*"));
        builder.Services.AddScoped<ICourseConfigEventPublisher, CourseConfigEventPublisher>();
        builder.Services.AddSlimMessageBus(mbb =>
        {
            mbb.AutoStartConsumersEnabled(false);
            mbb.WithProviderServiceBus(options =>
            {
                options.ConnectionString = connectionString;
                // Facilities topology is precreated by the VeloTime.MessageBus.Topology deployment job;
                // never reuse Timing's topic.
                options.TopologyProvisioning.Enabled = false;
            });
            mbb.AddJsonSerializer();
            mbb.WithHeaderModifier((headers, message) => headers["MessageName"] = message.GetType().Name);
            mbb.Produce<CourseConfigChangedV1>(producer => producer
                .DefaultTopic(CourseConfigTopic)
                .WithModifier((message, transportMessage) =>
                {
                    // One session per facility keeps a facility's course configuration changes in order.
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
        builder.AddOutboxMonitor<FacilityDbContext>("facility", "facility");
        return builder;
    }
}

internal sealed class TimingAssociationOutboxStartup(IMasterMessageBus bus) : IHostedService
{
    // Explicit Start propagates outbox schema initialization failures to host startup.
    public Task StartAsync(CancellationToken cancellationToken) => bus.Start();
    public Task StopAsync(CancellationToken cancellationToken) => bus.Stop();
}
