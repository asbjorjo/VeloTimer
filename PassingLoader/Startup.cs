using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTimer.PassingLoader.Commands;
using VeloTimer.PassingLoader.Handlers;
using VeloTimer.PassingLoader.Services.Messaging;

namespace VeloTimer.PassingLoader
{
    public static class Startup
    {
        public static IHostBuilder UsePassingLoader(this IHostBuilder builder)
        {
            builder
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddEnvironmentVariables("VELOTIME_");
                })
                .ConfigureServices((context, services) =>
                {
                    //services.ConfigureStorage(context.Configuration);
                    services.AddMessagingService(context.Configuration);
                    //services.AddExternalMessagingervice(context.Configuration);
                    //services.ConfigureApiClient(context.Configuration);
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(typeof(IMediator).Assembly));
                    services.AddScoped<IRequestHandler<SendTrackPassing>, SendTrackPassingHandler>();
                });

            return builder;
        }

        public static IHostBuilder UsePassingSubmitter(this IHostBuilder builder)
        {
            builder
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddEnvironmentVariables("VELOTIME_");
                })
                .ConfigureServices((context, services) =>
                {
                    //services.ConfigureStorage(context.Configuration);
                    services.AddMessagingService(context.Configuration);
                    services.AddExternalMessagingervice(context.Configuration);
                    //services.ConfigureApiClient(context.Configuration);
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(typeof(IMediator).Assembly));
                    services.AddScoped<IRequestHandler<RegisterTrackPassing>, RegisterTrackPassingHandler>();
                });

            return builder;
        }
    }
}
