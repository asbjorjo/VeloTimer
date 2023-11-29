using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTimer.PassingLoader.Behaviours;
using VeloTimer.PassingLoader.Commands;
using VeloTimer.PassingLoader.Handlers;
using VeloTimer.PassingLoader.Services.Messaging;

namespace VeloTimer.PassingLoader
{
    public static class Startup
    {
        internal static IHostBuilder UseCommon(this IHostBuilder builder)
        {
            builder
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddEnvironmentVariables("VELOTIME_");
                })
                .ConfigureServices((context, services) => 
                {
                    services.AddMessagingService(context.Configuration);
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(typeof(IMediator).Assembly));
                    services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
                });

            return builder;
        }

        public static IHostBuilder UsePassingLoader(this IHostBuilder builder)
        {
            builder
                .UseCommon()
                .ConfigureServices((context, services) =>
                {
                    //services.ConfigureStorage(context.Configuration);
                    //services.AddExternalMessagingervice(context.Configuration);
                    //services.ConfigureApiClient(context.Configuration);
                    services.AddScoped<IRequestHandler<SendTrackPassing>, SendTrackPassingHandler>();
                });

            return builder;
        }

        public static IHostBuilder UsePassingSubmitter(this IHostBuilder builder)
        {
            builder
                .UseCommon()
                .ConfigureServices((context, services) =>
                {
                    //services.ConfigureStorage(context.Configuration);
                    services.AddExternalMessagingService(context.Configuration);
                    //services.ConfigureApiClient(context.Configuration);
                    services.AddScoped<IRequestHandler<RegisterTrackPassing>, RegisterTrackPassingHandler>();
                });

            return builder;
        }
    }
}
