using Microsoft.Extensions.Hosting;
using VeloTime.Timing.Agents.PassingObserver;

IHostBuilder hostBuilder = Host
    .CreateDefaultBuilder(args)
    .UsePassingObserver();

hostBuilder.Start();
