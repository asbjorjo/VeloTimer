using Microsoft.Extensions.DependencyInjection;
using VeloTimerWeb.Api.Models;

namespace VeloTimerWeb.Api.Services
{
    public static class Startup
    {
        public static IServiceCollection AddVeloTimeServices(this IServiceCollection services)
        {
            services.AddScoped<IPassingService, PassingService>();
            services.AddScoped<IRiderService, RiderService>();
            services.AddScoped<IStatisticsService, StatisticsService>();
            services.AddScoped<ITrackService, TrackService>();
            services.AddScoped<ITransponderService, TransponderService>();

            //services.AddHostedService<CreatePassingHandler>();

            services.AddAutoMapper(typeof(VeloTimeProfile));

            return services;
        }
    }
}
