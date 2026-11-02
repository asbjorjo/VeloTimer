using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using StackExchange.Redis;

namespace VeloTime.Module.Facilities.Api;

public static class DataProtectionExtensions
{
    public const string ApplicationName = "VeloTime.Facilities.Api";
    public const string RedisKey = "velotime:facilities-api:dataprotection-keys";

    /// <summary>
    /// Shares the Facilities API key ring through the module cache Redis connection so timing
    /// association confirmation tokens issued by one replica can be validated by any other.
    /// Requires <c>AddModuleCache</c> (via <c>AddModuleFacilities</c>) to register
    /// <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    public static IHostApplicationBuilder AddFacilitiesDataProtection(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDataProtection()
            .SetApplicationName(ApplicationName);
        // Resolve the shared multiplexer on first key-ring access rather than while building
        // options: OpenAPI document generation constructs hosted services without a cache
        // connection string. A missing or unreachable Redis still fails explicitly when keys are
        // loaded or a token is protected; there is no ephemeral per-process fallback.
        builder.Services.AddOptions<KeyManagementOptions>()
            .Configure<IServiceProvider>((options, services) =>
                options.XmlRepository = new RedisXmlRepository(
                    () => services.GetRequiredService<IConnectionMultiplexer>().GetDatabase(), RedisKey));
        return builder;
    }
}
