using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VeloTimer.PassingLoader.Configuration;
using VeloTimer.Shared.Services.Api;
using VeloTimer.Shared.Services.Messaging;

namespace VeloTimer.PassingLoader
{
    public static class Startup
    {
        public static void ConfigurePassingLoader(this IServiceCollection services, IConfiguration config)
        {
            services.ConfigurePassingDatabase(config);
            services.ConfigureMessaging(config);
            services.ConfigureApiClient(config);
        }
    }
}
