using VeloTime.Module.Facilities.Interface.Client;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IHttpClientBuilder AddFacilitiesClient(this IServiceCollection services)
    {
        return services.AddHttpClient<IFacitiliesClient, HttpFacilitiesClient>(
            static client => client.BaseAddress = new("https+http://velotime-module-facilities-api/api/facilities/"));
    }
}
