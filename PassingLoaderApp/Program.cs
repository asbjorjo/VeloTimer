using Microsoft.Extensions.Hosting;
using VeloTime.X2.Core.Configuration;
using VeloTimer.PassingLoader;

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) => {
        services.ConfigurePassingLoader(context.Configuration);
        services.AddX2Service(context.Configuration);
        })
    .Build();

await host.RunAsync();