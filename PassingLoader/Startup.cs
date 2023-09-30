using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.PassingLoader.Services.Api;
using VeloTime.Shared.Messaging;
using VeloTimer.PassingLoader.Services.Storage;

namespace VeloTime.PassingLoader
{
    public static class Startup
    {
        public static void ConfigurePassingLoader(this IServiceCollection services, IConfiguration config)
        {
            services.ConfigureStorage(config);
            services.ConfigureMessaging(config);
            services.ConfigureApiClient(config);
            services.AddMessaging();
        }
    }
}
