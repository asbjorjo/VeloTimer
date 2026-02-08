using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.Module.Timing.Endpoints;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing;

public static class StartupExtensions
{
    public static IHostApplicationBuilder AddModuleTiming(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource("VeloTime.Module.Timing"))
            .WithMetrics(metrics => metrics.AddMeter("VeloTime.Module.Timing"));

        services.AddDbContext<TimingDbContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("TimingDbConnection"));
            options.UseSnakeCaseNamingConvention();
        });

        builder.AddModuleCache();
        services.AddScoped<InstallationService>();
        
        services.AddSingleton<Metrics>();

        return builder;
    }

    public static void UseModuleTiming(this IEndpointRouteBuilder app)
    {
        var timing = app.MapGroup("/api/timing")
            .WithTags(["Timing"]);

        timing.MapInstallationEndpoints();
        timing.MapSampleEndpoints();
    }
}
