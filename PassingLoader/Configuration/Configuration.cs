using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PassingLoader.Services;
using VeloTimer.Shared.Services;

namespace PassingLoader.Configuration
{
    public class Services
    {
        public static void ConfigureX2Service(HostBuilderContext context, IServiceCollection services)
        {
            services.AddX2Service(context.Configuration);
        }
    }

    public static class Configuration
    {
        public static IServiceCollection AddX2Service(this IServiceCollection services, IConfiguration config)
        {
            return services
                .Configure<MylapsX2Options>(config.GetSection(MylapsX2Options.MylapsX2))
                .AddSingleton<IMylapsX2Service, MylapsX2Service>()
                .AddHostedService<MylapsX2Processor>();
        }
    }
}
