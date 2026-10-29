using OpenTelemetry.Trace;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Outbox.PostgreSql.DbContext;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api;

public static class MessagingExtensions
{
    public static IHostApplicationBuilder AddOwnershipMessaging(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("MessageBus");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:MessageBus is required for ownership change events.");

        builder.Services.ConfigureOpenTelemetryTracerProvider(tracer => tracer.AddSource("Azure.Messaging.*"));
        builder.Services.AddSlimMessageBus(mbb =>
        {
            mbb.AutoStartConsumersEnabled(false);
            mbb.WithProviderServiceBus(options =>
            {
                options.ConnectionString = connectionString;
                // The existing Timing topic is provisioned externally, so broker outages do not block API startup.
                options.TopologyProvisioning.Enabled = false;
            });
            mbb.AddJsonSerializer();
            mbb.WithHeaderModifier((headers, message) => headers["MessageName"] = message.GetType().Name);
            mbb.Produce<TransponderOwnershipChanged>(producer => producer
                .DefaultTopic("velotime-timing-test")
                .WithModifier((message, transportMessage) =>
                {
                    transportMessage.SessionId = message.TransponderId.ToString();
                    transportMessage.MessageId = message.EventId.ToString();
                }));
            mbb.UseOutbox();
            mbb.AddOutboxUsingDbContext<TimingDbContext>(options =>
            {
                options.PostgreSqlSettings.DatabaseSchemaName = "timing";
                options.MaintainSequence = true;
                // A failed oldest event must not be overtaken by a later event in the same batch.
                options.PollBatchSize = 1;
                options.PollIdleSleep = TimeSpan.FromSeconds(5);
                options.MaxDeliveryAttempts = int.MaxValue;
                options.MessageCleanup = new OutboxMessageCleanupSettings
                {
                    Enabled = true,
                    BatchSize = 100,
                    Age = TimeSpan.FromDays(3),
                    Interval = TimeSpan.FromHours(1)
                };
            });
        });
        builder.Services.AddHostedService<OwnershipOutboxStartup>();
        return builder;
    }
}

internal sealed class OwnershipOutboxStartup(IMasterMessageBus bus) : IHostedService
{
    // Unlike AutoStart, Start propagates migration failures instead of logging and continuing startup.
    public Task StartAsync(CancellationToken cancellationToken) => bus.Start();
    public Task StopAsync(CancellationToken cancellationToken) => bus.Stop();
}
