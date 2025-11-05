using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using VeloTime.Agent.Messaging;
using VeloTimer.PassingLoader.Services.Api;

namespace VeloTime.Agent;

public static class StartupExtensions
{
    private static IHostEnvironment? _env;
    public static IHostApplicationBuilder ConfigureServices(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        configuration.AddEnvironmentVariables("VELOTIME_");

        _env = builder.Environment;

        services.AddSerilog(lc => lc
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}")
            .Enrich.FromLogContext()
            .ReadFrom.Configuration(configuration)
            );

        //services.ConfigureApiClient(configuration);
        services.ConfigureMessaging(configuration);

        return builder;
    }
}
