using VeloTime.Module.Timing.Interface.Client;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IHttpClientBuilder AddTimingClient(this IServiceCollection services)
    {
        return services.AddHttpClient<ITimingClient, TimingHttpClient>(
            static client => client.BaseAddress = new("http://module-timing-api/api/timing/"));
    }
}
