using Microsoft.Extensions.Hosting;
using VeloTime.Timing.Agents.EventSubmitter;
using VeloTime.Timing.Agents.EventSubmitter.Services;
using VeloTime.Timing.Services;

IHostBuilder hostBuilder = Host
    .CreateDefaultBuilder(args)
    //.AddServiceBusMessaging()
    .AddLoggingMessaging()
    .AddMasstransitMessaging()
    .AddMassTransit()
    .AddStartupService();

using IHost host = hostBuilder.Build();

await host.RunAsync();