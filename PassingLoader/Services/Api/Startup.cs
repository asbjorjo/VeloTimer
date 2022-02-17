using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace VeloTime.PassingLoader.Services.Api
{
    public static class Startup
    {
        public static IServiceCollection ConfigureApiClient(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpClient<IApiService, ApiService>(
                client => client.BaseAddress = new Uri(new Uri(configuration["VELOTIMER_API_URL"]), "api/"))
                .ConfigurePrimaryHttpMessageHandler<VeloHttpClientHandler>();

            services.TryAddTransient<VeloHttpClientHandler>();

            return services;
        }
    }

    internal class VeloHttpClientHandler : HttpClientHandler
    {
        public VeloHttpClientHandler()
        {
            MaxConnectionsPerServer = 20;
        }
    }
}
