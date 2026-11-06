using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Notifications.Interface.Messages;
using VeloTime.Module.Notifications.Processor.Workers;

namespace VeloTime.Module.Notifications.Processor;

public static class NotificationsProcessorExtensions
{
    public static IHostApplicationBuilder AddNotificationsProcessor(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("MessageBus");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:MessageBus is required for Notifications refresh events.");
        }

        builder.Services.ConfigureOpenTelemetryTracerProvider(tracer => tracer.AddSource("Azure.Messaging.*"));
        builder.Services.AddOptions<NotificationWorkerOptions>()
            .Bind(builder.Configuration.GetSection(NotificationWorkerOptions.Section))
            .ValidateDataAnnotations()
            .Validate(options => options.RetryMaximumDelaySeconds >= options.RetryInitialDelaySeconds,
                "RetryMaximumDelaySeconds must be at least RetryInitialDelaySeconds.")
            .ValidateOnStart();

        builder.Services.AddSlimMessageBus(mbb =>
        {
            // This processor only publishes refresh hints. The topic and WebUI subscription are
            // provisioned by the topology deployment job, never by an application replica.
            mbb.AutoStartConsumersEnabled(false);
            mbb.WithProviderServiceBus(options =>
            {
                options.ConnectionString = connectionString;
                options.TopologyProvisioning.Enabled = false;
            });
            mbb.AddJsonSerializer();
            mbb.WithHeaderModifier((headers, message) =>
                headers[NotificationMessaging.MessageNameHeader] = message.GetType().Name);
            mbb.Produce<NotificationsRefreshRequestedV1>(producer => producer
                .DefaultTopic(NotificationMessaging.Topic)
                .WithModifier((message, transportMessage) =>
                    transportMessage.MessageId = message.EventId.ToString()));
        });

        builder.Services.AddSingleton<INotificationRefreshPublisher, SlimMessageBusNotificationRefreshPublisher>();
        builder.Services.AddScoped<NotificationWorkDispatcher>();
        builder.Services.AddHostedService<NotificationRefreshBusStartup>();
        builder.Services.AddHostedService<NotificationWorkDispatcherWorker>();
        return builder;
    }
}

internal sealed class NotificationRefreshBusStartup(IMasterMessageBus bus) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => bus.Start();

    public Task StopAsync(CancellationToken cancellationToken) => bus.Stop();
}
