using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PassingLoader.Configuration;
using VeloTimer.PassingLoader;
using VeloTimer.Shared.Services;

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) => {
        services.ConfigurePassingLoader(context.Configuration);
        services.AddX2Service(context.Configuration);
        })
    .Build();

var api = host.Services.GetRequiredService<IApiService>();
var mostrecent = await api.GetMostRecentPassing();

DateTime startTime = DateTime.MinValue;

if (mostrecent != null) startTime = mostrecent.Time.ToUniversalTime() - TimeSpan.FromMinutes(15);

var x2 = host.Services.GetRequiredService<IMylapsX2Service>();
x2.ProcessFrom(startTime);

await host.RunAsync();