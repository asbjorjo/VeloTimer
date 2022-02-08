using Microsoft.Extensions.DependencyInjection;

namespace VeloTimerWeb.Api.Services
{
    public static class Startup
    {
        public static IServiceCollection AddVeloTimeServices(this IServiceCollection services)
        {
            services.AddScoped<IActivityService, ActivityService>();
            services.AddScoped<IPassingService, PassingService>();
            services.AddScoped<IRiderService, RiderService>();
            services.AddScoped<IStatisticsService, StatisticsService>();
            services.AddScoped<ITrackService, TrackService>();
            services.AddScoped<ITransponderService, TransponderService>();

            services.AddHostedService<CreatePassingHandler>();

            return services;
        }
    }
}
