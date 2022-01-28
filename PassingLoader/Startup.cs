using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VeloTimer.PassingLoader.Services.Api;
using VeloTimer.PassingLoader.Services.Messaging;

namespace VeloTimer.PassingLoader
{
    public static class Startup
    {
        public static void ConfigurePassingLoader(this IServiceCollection services, IConfiguration config)
        {
            services.ConfigureMessaging(config);
            services.ConfigureApiClient(config);
        }
    }
}
