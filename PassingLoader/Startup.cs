using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VeloTimer.PassingLoader.Services.Api;
using VeloTimer.PassingLoader.Services.Messaging;
using VeloTimer.PassingLoader.Services.Storage;

namespace VeloTimer.PassingLoader
{
    public static class Startup
    {
        public static void ConfigurePassingLoader(this IServiceCollection services, IConfiguration config)
        {
            services.ConfigureStorage(config);
            services.ConfigureMessaging(config);
            services.ConfigureApiClient(config);
        }
    }
}
