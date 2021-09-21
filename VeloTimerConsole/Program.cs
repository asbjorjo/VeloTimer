using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTimerConsole.Data;
using VeloTimerConsole.Services;

namespace VeloTimerConsole
{
    class Program
    {
        static Task Main(string[] args)
        {
            using IHost host = CreateHostBuilder(args).Build();

            return host.RunAsync();
        }

        private static IHostBuilder CreateHostBuilder(string[] args)
        {
            var hostBuilder = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, configuration) =>
                {
                    configuration.Sources.Clear();
                    IHostEnvironment env = hostingContext.HostingEnvironment;
                    configuration
                        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
                    IConfigurationRoot configurationRoot = configuration.Build();
                })
                .ConfigureServices((context, services) => 
                { 
                    var configuration = context.Configuration;
                    services.Configure<PassingDatabaseSettings>(
                                configuration.GetSection(nameof(PassingDatabaseSettings)))
                            .AddSingleton<AmmcPassingService>();
                });
            return hostBuilder;
        }
    }
}
