using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTimer.PassingLoader.Services.Api;
using VeloTimer.PassingLoader.Services.Messaging;
using VeloTimer.PassingLoader.Services.Storage;

namespace VeloTimer.PassingLoader
{
    internal static class Startup
    {
        internal static IHostBuilder ConfigurePassingLoader(this IHostBuilder builder)
        {
            builder
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddEnvironmentVariables("VELOTIME_");
                })
                .ConfigureServices((context, services) =>
                {
                    //services.ConfigureStorage(context.Configuration);
                    services.ConfigureMessaging(context.Configuration);
                    services.ConfigureApiClient(context.Configuration);
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(typeof(Startup).Assembly));
                });

            return builder;
        }
    }
}
