using OpenTelemetry.Trace;
using Azure.Messaging.ServiceBus.Administration;
using VeloTime.MessageBus.Topology;

var builder = Host.CreateApplicationBuilder(args);

// The topology file replaces (never merges with) the bundled default topology.
var topologyFile = builder.Configuration[MessageBusTopologyOptions.TopologyFileKey];
builder.Configuration.AddJsonFile(
    string.IsNullOrWhiteSpace(topologyFile)
        ? Path.Combine(AppContext.BaseDirectory, "topology.json")
        : topologyFile,
    optional: false,
    reloadOnChange: false);

builder.AddServiceDefaults();
builder.Services.ConfigureOpenTelemetryTracerProvider(tracer => tracer
    .AddSource(TopologyWorker.ActivitySourceName)
    .AddSource("Azure.Messaging.*"));

builder.Services.Configure<MessageBusTopologyOptions>(
    builder.Configuration.GetSection(MessageBusTopologyOptions.Section));

builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString(MessageBusTopologyOptions.ConnectionStringName);
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException(
            $"ConnectionStrings:{MessageBusTopologyOptions.ConnectionStringName} is required; it needs Manage rights on the Service Bus namespace.");
    return new ServiceBusAdministrationClient(connectionString);
});
builder.Services.AddSingleton<IServiceBusTopologyClient, ServiceBusTopologyClient>();
builder.Services.AddSingleton<MessageBusTopologyReconciler>();
builder.Services.AddHostedService<TopologyWorker>();

var host = builder.Build();
host.Run();
