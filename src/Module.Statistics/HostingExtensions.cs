using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using VeloTime.Module.Statistics.Endpoints;
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

        //services.AddDbContext<StatisticsDbContext>(options =>
        //{
        //    options.UseNpgsql(configuration.GetConnectionString("StatisticsDbConnection"));
        //    options.UseSnakeCaseNamingConvention();
        //});

        builder.AddModuleStorage<StatisticsDbContext>(connectionName: "velotimedb");

        services.AddTransient<StatisticsService>();
        services.AddSingleton<Metrics>();

        services.AddPagination();

        builder.AddModuleCache();

        return builder;
    }

    public static void UseModuleStatistics(this WebApplication app)
    {
        var statistics = app.MapGroup("/api/statistics").WithTags(["Statistics"]);

        statistics.MapSampleEndpoints();
    }
}
