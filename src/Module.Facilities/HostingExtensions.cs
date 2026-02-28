using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using VeloTime.Module.Facilities.Endpoints;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities;

public static class StartupExtensions
{
    public static IHostApplicationBuilder AddModuleFacilities(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        services.ConfigureOpenTelemetryTracerProvider(tracer =>
        {
            tracer.AddSource("VeloTime.Module.Facilities");
        });
        services.ConfigureOpenTelemetryMeterProvider(metrics =>
        {
            metrics.AddMeter("VeloTime.Module.Facilities");
        });

        //services.AddSlimMessageBus(mbb =>
        //{
        //    mbb.Consume<PassingSaved>(x => x
        //        .Topic("velotime-timing-test")
        //        .SubscriptionName("statistics")
        //        .WithConsumer<PassingSavedHandler>()
        //        .Instances(1)
        //        .EnableSession());
        //    mbb.AddServicesFromAssemblyContaining<PassingSavedHandler>();
        //});

        //services.AddDbContext<FacilityDbContext>(options =>
        //{
        //    options.UseNpgsql(configuration.GetConnectionString("FacilityDbConnection"));
        //    options.UseSnakeCaseNamingConvention();
        //});

        builder.AddModuleStorage<FacilityDbContext>(connectionName: "velotimedb");

        services.AddTransient<FacilitiesService>();
        services.AddSingleton<Metrics>();
        builder.AddModuleCache();

        return builder;
    }

    public static void UseModuleFacilities(this IEndpointRouteBuilder app)
    {
        var facilities = app.MapGroup("/api/facilities").WithTags(["Facilities"]);

        facilities.MapCourseLayoutEndpoints();
        facilities.MapCoursePointEndpoints();
        facilities.MapFacilityEndpoints();
    }
}
