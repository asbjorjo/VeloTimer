using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace VeloTime.Bootstrap;

public static class HostingExtensions
{
    public static IHostApplicationBuilder AddBootstrap(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddHostedService<Worker>();

        return builder;
    }
}
