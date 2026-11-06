using VeloTime.Module.Statistics.Client;

namespace Microsoft.Extensions.DependencyInjection;

public static class Extensions
{
    public static IHttpClientBuilder AddStatisticsClient(this IServiceCollection services)
    {
        var httpBuilder = services.AddHttpClient<IStatisticsClient, StatisticsClient>(
            static client => client.BaseAddress = new("https+http://module-statistics-api"));
        return httpBuilder;
    }
}
