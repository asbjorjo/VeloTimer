using OpenTelemetry.Trace;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Timing.Messaging;

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
                // The Timing topic is provisioned by the VeloTime.MessageBus.Topology deployment job,
                // so broker outages do not block API startup.
                options.TopologyProvisioning.Enabled = false;
            });
            mbb.AddJsonSerializer();
            mbb.WithHeaderModifier((headers, message) => headers["MessageName"] = message.GetType().Name);
            mbb.AddTimingEventsWithOutbox();
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
