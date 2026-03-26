using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics;

public static class StartupExtensions
{
    public static IHostApplicationBuilder AddModuleStatistics(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        services.ConfigureOpenTelemetryTracerProvider(tracer =>
        {
            tracer.AddSource("VeloTime.Module.Statistics");
        });
        services.ConfigureOpenTelemetryMeterProvider(metrics =>
        {
            metrics.AddMeter("VeloTime.Module.Statistics");
        });

        builder.AddModuleStorage<StatisticsDbContext>(connectionName: "velotimedb");

        services.AddTransient<StatisticsService>();
        services.AddSingleton<Metrics>();

        services.AddPagination();

        builder.AddModuleCache();

        return builder;
    }

    public static void UseModuleStatistics(this WebApplication app)
    {
    }
}
