using VeloTime.Module.Profiles.Client;

namespace Microsoft.Extensions.DependencyInjection;

public static class Extensions
{
    public static IHttpClientBuilder AddProfilesClient(this IServiceCollection services)
    {
        return services.AddHttpClient<IProfilesClient, ProfilesClient>(
            static client => client.BaseAddress = new("https+http://module-profiles-api"));
    }
}
