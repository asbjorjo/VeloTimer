var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddServices();

using var host = builder.Build();
// Resolving and explicitly starting the master also starts the stock outbox poller
// on an idle process, before any incoming message happens to resolve a scoped bus.
await VeloTime.Module.Statistics.Service.StatisticsProcessorStartup.StartStatisticsProcessorAsync(host);
await host.RunAsync();
