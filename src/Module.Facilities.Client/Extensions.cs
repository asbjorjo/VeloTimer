using VeloTime.Module.Facilities.Client;

namespace Microsoft.Extensions.DependencyInjection;

public static class Extensions
{
    public static IHttpClientBuilder AddFacilitiesClient(this IServiceCollection services)
    {
        var httpBuilder = services.AddHttpClient<IFacilitiesClient, FacilitiesClient>(
            static client => client.BaseAddress = new("https+http://module-facilities-api"));
        return httpBuilder;
    }
}
