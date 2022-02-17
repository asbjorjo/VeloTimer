using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VeloTimer.PassingLoader.Services.Api;
using VeloTime.Shared.Messaging;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTimer.PassingLoader
{
    public static class Startup
    {
        public static void ConfigurePassingLoader(this IServiceCollection services, IConfiguration config)
        {
            services.ConfigureMessaging(config);
            services.ConfigureApiClient(config);
            services.AddMessaging();
        }
    }
}
