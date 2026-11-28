using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
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

        services.ConfigureOpenTelemetryTracerProvider(tracer =>
        {
            tracer.AddSource("VeloTime.Module.Timing");
        });
        services.ConfigureOpenTelemetryMeterProvider(metrics =>
        {
            metrics.AddMeter("VeloTime.Module.Timing");
        });

        builder.AddModuleStorage<TimingDbContext>(connectionName: "velotimedb");

        builder.AddModuleCache();
        services.AddOptions<TimingOptions>().Bind(configuration.GetSection(TimingOptions.SectionName));
        services.AddScoped<InstallationService>();
        services.AddScoped<LoopStatusService>();
        services.AddScoped<TransponderService>();
        services.AddScoped<PassingReplayRequestService>();
        
        services.AddSingleton<Metrics>();

        return builder;
    }

    public static void UseModuleTiming(this IEndpointRouteBuilder app)
    {
    }
}
