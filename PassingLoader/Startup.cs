using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VeloTimer.PassingLoader.Configuration;
using VeloTimer.PassingLoader.Services;
using VeloTimer.Shared.Configuration;
using VeloTimer.Shared.Services;

namespace VeloTimer.PassingLoader
{
    public static class Startup
    {
        public static void ConfigurePassingLoader(this IServiceCollection services, IConfiguration config)
        {
            services.ConfigurePassingDatabase(config);
            services.ConfigureMessaging(config);
            services.AddHttpClient<IApiService, ApiService>(
                client => client.BaseAddress = new Uri(new Uri(config["VELOTIMER_API_URL"]), "api/"))
                .ConfigurePrimaryHttpMessageHandler<VeloHttpClientHandler>();
            services.AddPassingLoader();
        }

        public static IServiceCollection AddPassingLoader(this IServiceCollection services)
        {
            services.TryAddTransient<VeloHttpClientHandler>();
            services.TryAddSingleton<IMessagingService, MessagingService>();
            
            return services;
        }
    }
    
    public class VeloHttpClientHandler : HttpClientHandler
    {
        public VeloHttpClientHandler()
        {
            MaxConnectionsPerServer = 20;
        }
    }
}
