using VeloTime.Module.Statistics.Interface.Client;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IHttpClientBuilder AddStatisticsClient(this IServiceCollection services)
    {
        return services.AddHttpClient<IStatisticsClient, HttpStatisticsClient>(
            static client => client.BaseAddress = new("https+http://module-statistics-api/api/statistics/"));
    }
}
