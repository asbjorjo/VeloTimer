using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class FacilitiesDataProtectionTests
{
    [Fact]
    public void Registration_UsesFacilitiesApplicationAndSharedRedisKeyRingWithoutConnectingEagerly()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddFacilitiesDataProtection();
        using var services = builder.Services.BuildServiceProvider();

        // No IConnectionMultiplexer is registered: options must still build (OpenAPI generation).
        var keyManagement = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.IsType<RedisXmlRepository>(keyManagement.XmlRepository);
        Assert.Equal(DataProtectionExtensions.ApplicationName,
            services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
        Assert.Equal("velotime:facilities-api:dataprotection-keys", DataProtectionExtensions.RedisKey);
    }

    [RedisCacheFact]
    public async Task ConfirmationToken_IssuedByOneReplica_IsAcceptedByAnotherSharingRedis()
    {
        var redis = Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")!;
        await using var replicaA = await ReplicaAsync(redis);
        await using var replicaB = await ReplicaAsync(redis);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var storage = new FacilityDbContext(new DbContextOptionsBuilder<FacilityDbContext>().UseSqlite(connection).Options);
        var requested = new TimingAssociationRequestedChange(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, null);
        IReadOnlyList<TimingAssociationDestinationConflict> conflicts = [];
        IReadOnlyList<CoursePointTimingAssociationSnapshot> source = [];

        var token = new TimingAssociationService(storage, replicaA.GetRequiredService<IDataProtectionProvider>())
            .CreateConfirmationToken(requested, conflicts, source);

        Assert.True(new TimingAssociationService(storage, replicaB.GetRequiredService<IDataProtectionProvider>())
            .ConfirmationMatches(token, requested, conflicts, source));
        Assert.False(new TimingAssociationService(storage, new EphemeralDataProtectionProvider())
            .ConfirmationMatches(token, requested, conflicts, source));
    }

    private static async Task<ServiceProvider> ReplicaAsync(string redis)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IConnectionMultiplexer>(await ConnectionMultiplexer.ConnectAsync(redis));
        builder.AddFacilitiesDataProtection();
        return builder.Services.BuildServiceProvider();
    }
}
