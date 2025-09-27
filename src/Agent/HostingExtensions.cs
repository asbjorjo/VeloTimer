using Microsoft.Extensions.Hosting;
using Serilog;

namespace VeloTime.Agent;

public static class StartupExtensions
{
    private static IHostEnvironment? _env;
    public static IHostApplicationBuilder ConfigureServices(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        _env = builder.Environment;

        services.AddSerilog(lc => lc
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}")
            .Enrich.FromLogContext()
            .ReadFrom.Configuration(configuration)
            );

        return builder;
    }
}
