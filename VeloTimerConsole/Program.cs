using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using VeloTimerConsole.Data;
using VeloTimerConsole.Services;

namespace VeloTimerConsole
{
    class Program
    {
        static async Task Main(string[] args)
        {
            using IHost host = CreateHostBuilder(args).Build();

            await host.RunAsync();
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, configuration) =>
                {
                    configuration.Sources.Clear();
                    IHostEnvironment env = hostingContext.HostingEnvironment;
                    configuration
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
                    IConfigurationRoot configurationRoot = configuration.Build();
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;
                    services.Configure<PassingDatabaseSettings>(
                                configuration.GetSection(nameof(PassingDatabaseSettings)));
                    services.AddSingleton<IPassingDatabaseSettings>(sp =>
                                sp.GetRequiredService<IOptions<PassingDatabaseSettings>>().Value);
                    services.AddSingleton<AmmcPassingService>();
                    services.AddHttpClient("VeloTimerWeb.ServerAPI", client => client.BaseAddress = new Uri("https://velotimer-api.azurewebsites.net"));
                    services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("VeloTimerWeb.ServerAPI"));
                    services.AddHostedService<RefreshPassingsService>();
                });
    }
}
