using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;

namespace VeloTime.Module.Common.Operations;

/// <summary>
/// Registers the delivery-health monitors (plan Phase 5). Settings live under <c>Messaging:Monitoring</c>:
/// <c>Enabled</c> (default true), <c>OutboxSampleInterval</c>, <c>DeadLetterSampleInterval</c> and <c>DeadLetterPeekLimit</c>.
/// </summary>
public static class OperationsMonitoringExtensions
{
    public const string Section = "Messaging:Monitoring";

    /// <summary>Samples the SlimMessageBus PostgreSQL outbox in <paramref name="schema"/> of <typeparamref name="TContext"/>'s database.</summary>
    public static IHostApplicationBuilder AddOutboxMonitor<TContext>(this IHostApplicationBuilder builder, string name, string schema)
        where TContext : DbContext
    {
        if (!Enabled(builder)) return builder;
        AddMetrics(builder);
        var interval = builder.Configuration.GetValue($"{Section}:OutboxSampleInterval", TimeSpan.FromSeconds(30));
        builder.Services.AddHostedService(provider => ActivatorUtilities.CreateInstance<OutboxMonitor<TContext>>(provider,
            new OutboxMonitorTarget(name, schema, interval)));
        return builder;
    }

    /// <summary>
    /// Watches the dead-letter queues of the given subscriptions with the host's <c>ConnectionStrings:MessageBus</c>.
    /// Skipped when that connection string is missing (local runs without Service Bus).
    /// </summary>
    public static IHostApplicationBuilder AddDeadLetterMonitor(this IHostApplicationBuilder builder, params DeadLetterSubscription[] subscriptions)
    {
        var connectionString = builder.Configuration.GetConnectionString("MessageBus");
        if (!Enabled(builder) || string.IsNullOrWhiteSpace(connectionString) || subscriptions.Length == 0) return builder;
        AddMetrics(builder);
        var options = new DeadLetterMonitorOptions
        {
            SampleInterval = builder.Configuration.GetValue($"{Section}:DeadLetterSampleInterval", TimeSpan.FromMinutes(1)),
            PeekLimit = Math.Clamp(builder.Configuration.GetValue($"{Section}:DeadLetterPeekLimit", 1000), 1, 10_000),
            Subscriptions = subscriptions
        };
        builder.Services.AddHostedService(provider => new DeadLetterMonitor(new ServiceBusClient(connectionString),
            provider.GetRequiredService<MessagingMetrics>(), options, provider.GetRequiredService<ILogger<DeadLetterMonitor>>()));
        return builder;
    }

    private static bool Enabled(IHostApplicationBuilder builder) => builder.Configuration.GetValue($"{Section}:Enabled", true);

    private static void AddMetrics(IHostApplicationBuilder builder)
    {
        builder.Services.AddMetrics();
        builder.Services.TryAddSingleton<MessagingMetrics>();
        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(MessagingMetrics.MeterName));
    }
}
