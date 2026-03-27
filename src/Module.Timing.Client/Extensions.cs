using VeloTime.Module.Timing.Client;

namespace Microsoft.Extensions.DependencyInjection;

public static class Extensions
{
    public static IHttpClientBuilder AddTimingClient(this IServiceCollection services)
    {
        var httpBuilder = services.AddHttpClient<ITimingClient, TimingClient>(
            static client => client.BaseAddress = new("https+http://module-timing-api"));
        return httpBuilder;
    }
}
