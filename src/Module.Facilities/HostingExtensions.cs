using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Facilities.Endpoints;
using VeloTime.Module.Facilities.Interface.Client;
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

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource("VeloTime.Module.Facilities"))
            .WithMetrics(metrics => metrics.AddMeter("VeloTime.Module.Facilities"));

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

        services.AddDbContext<FacilityDbContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("StatisticsDbConnection"));
            options.UseSnakeCaseNamingConvention();
        });

        services.AddTransient<FacilitiesService>();
        services.AddTransient<IFacitiliesClient, FacilitiesClient>();
        services.AddSingleton<Metrics>();

        return builder;
    }

    public static void UseModuleFacilities(this WebApplication app)
    {
        var facilities = app.MapGroup("/api/facilities").WithTags(["Facilities"]);

        facilities.MapCourseLayoutEndpoints();
        facilities.MapCoursePointEndpoints();
        facilities.MapFacilityEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.ApplyMigrations<FacilityDbContext>();
        }
    }
}
