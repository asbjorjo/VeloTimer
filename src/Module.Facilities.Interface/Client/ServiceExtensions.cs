using VeloTime.Module.Facilities.Interface.Client;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddFacilitiesClient(this IServiceCollection services)
    {
        services.AddHttpClient<IFacitiliesClient, HttpFacilitiesClient>(
            static client => client.BaseAddress = new("https+http://module-facilities-api/api/facilities/"));
        return services;
    }
}
