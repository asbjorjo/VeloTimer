using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.Module.Profiles.Service;
using VeloTime.Module.Profiles.Storage;

namespace VeloTime.Module.Profiles.Api.Test;

public sealed class ProfilePreferenceCacheTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private const string LanguageRoute = "/api/profiles/me/preferences";
    private const string DisplayRoute = "/api/profiles/me/preferences/display";

    [Fact]
    public async Task LanguageReads_UseVersionedIdentityScopedKeyAndExplicitExpiration()
    {
        using var factory = new ProfilesApiFactory();
        using var client = factory.CreateContractClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(LanguageRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(LanguageRoute)).StatusCode);

        Assert.Single(factory.Preferences.Calls);
        Assert.Equal(2, factory.Cache.Reads.Count);
        var key = Assert.Single(factory.Cache.Reads.Select(read => read.Key).Distinct());
        Assert.StartsWith("velotime:profiles:language-preference:persisted-v1:", key, StringComparison.Ordinal);
        Assert.DoesNotContain("issuer.example", key, StringComparison.Ordinal);
        Assert.DoesNotContain("subject", key, StringComparison.Ordinal);
        Assert.All(factory.Cache.Reads, read =>
        {
            Assert.Equal(TimeSpan.FromSeconds(30), read.Options?.Expiration);
            Assert.Equal(TimeSpan.FromSeconds(30), read.Options?.LocalCacheExpiration);
        });
    }

    [Fact]
    public async Task LanguageReads_IsolateExactIdentityAndReuseOnlyMatchingIdentity()
    {
        using var factory = new ProfilesApiFactory();
        using var client = factory.CreateContractClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(LanguageRoute)).StatusCode);
        factory.Principal = ProfilesApiFactory.Identity(" https://issuer.example/realm", "subject");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(LanguageRoute)).StatusCode);
        factory.Principal = ProfilesApiFactory.Identity("https://issuer.example/realm", "subject");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(LanguageRoute)).StatusCode);

        Assert.Equal(2, factory.Preferences.Calls.Count);
        Assert.Equal(3, factory.Cache.Reads.Count);
        Assert.Equal(2, factory.Cache.Reads.Select(read => read.Key).Distinct().Count());
        Assert.Equal(" https://issuer.example/realm", factory.Preferences.Calls[1].Issuer);
    }

    [Fact]
    public async Task InvalidIdentity_IsRejectedBeforeUsingWarmCacheEntry()
    {
        using var factory = new ProfilesApiFactory();
        using var client = factory.CreateContractClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(LanguageRoute)).StatusCode);

        factory.Principal = ProfilesApiFactory.Identity(null, "subject");
        using var response = await client.GetAsync(LanguageRoute);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(factory.Preferences.Calls);
        Assert.Single(factory.Cache.Reads);
    }

    [Fact]
    public async Task SuccessfulLanguageWrite_InvalidatesOnlyIdentityKeyAndNextReadReloads()
    {
        using var factory = new ProfilesApiFactory();
        using var client = factory.CreateContractClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(LanguageRoute)).StatusCode);

        using var write = await client.PutAsJsonAsync(LanguageRoute + "/language", new UpdateLanguagePreferenceRequest("nb-NO"));
        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        Assert.Single(factory.Cache.Removals);
        Assert.Equal(factory.Cache.Reads[0].Key, factory.Cache.Removals[0].Key);
        Assert.True(factory.Cache.Removals[0].Token.CanBeCanceled);
        Assert.NotEqual(factory.Preferences.Calls.Single(call => call.Write).Token, factory.Cache.Removals[0].Token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(LanguageRoute)).StatusCode);
        Assert.Equal(3, factory.Preferences.Calls.Count);
    }

    [Fact]
    public async Task FailedLanguageWrite_DoesNotEvictWarmRead()
    {
        using var factory = new ProfilesApiFactory();
        using var client = factory.CreateContractClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(LanguageRoute)).StatusCode);
        factory.Preferences.OnCall = _ => Task.FromException<LanguagePreferenceDTO>(new InvalidOperationException("write failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PutAsJsonAsync(LanguageRoute + "/language", new UpdateLanguagePreferenceRequest("nb-NO")));

        factory.Preferences.OnCall = null;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(LanguageRoute)).StatusCode);
        Assert.Empty(factory.Cache.Removals);
        Assert.Equal(2, factory.Preferences.Calls.Count);
    }

    [Fact]
    public async Task InvalidationFailure_AfterSuccessfulWrite_IsVisible()
    {
        using var factory = new ProfilesApiFactory();
        using var client = factory.CreateContractClient();
        var failure = new InvalidOperationException("cache unavailable");
        factory.Cache.RemoveFailure = failure;

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PutAsJsonAsync(LanguageRoute + "/language", new UpdateLanguagePreferenceRequest("nb-NO")));

        Assert.Same(failure, actual);
        Assert.Single(factory.Cache.Removals);
        Assert.True(factory.Preferences.Calls.Single().Write);
    }

    [PostgresFact]
    public async Task DisplayReads_UseDisplayPreferenceCacheKeyAndWarmValue()
    {
        var issuer = "display-cache-" + Guid.NewGuid();
        var subject = Guid.NewGuid().ToString();
        using var root = RealPreferencesFactory(issuer, subject);
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(ConfigureStorage));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var initial = await client.GetFromJsonAsync<DisplayPreferenceDTO>(DisplayRoute);
        Assert.Equal(new DisplayPreferenceDTO("m", "km/h", "browser", null), initial);

        // Change persistence directly; a subsequent identical request must still use the cached value.
        await using (var context = database.CreateContext())
            await new DisplayPreferenceService(context).SetAsync(
                issuer, subject, new UpdateDisplayPreferenceRequest("ft", "mph", "utc", null));
        Assert.Equal(initial, await client.GetFromJsonAsync<DisplayPreferenceDTO>(DisplayRoute));

        Assert.Equal(2, root.Cache.Reads.Count);
        var key = Assert.Single(root.Cache.Reads.Select(read => read.Key).Distinct());
        Assert.StartsWith("velotime:profiles:display-preference:persisted-v1:", key, StringComparison.Ordinal);
        Assert.DoesNotContain(issuer, key, StringComparison.Ordinal);
        Assert.DoesNotContain(subject, key, StringComparison.Ordinal);
        Assert.All(root.Cache.Reads, read =>
        {
            Assert.Equal(TimeSpan.FromSeconds(30), read.Options?.Expiration);
            Assert.Equal(TimeSpan.FromSeconds(30), read.Options?.LocalCacheExpiration);
        });
    }

    [PostgresFact]
    public async Task DisplayReads_IsolateExactIdentityAndReuseOnlyMatchingIdentity()
    {
        var issuer = "https://issuer.example/realm/" + Guid.NewGuid();
        var subject = Guid.NewGuid().ToString();
        var alternateIssuer = " " + issuer;
        await using (var context = database.CreateContext())
        {
            var preferences = new DisplayPreferenceService(context);
            await preferences.SetAsync(issuer, subject, new("ft", "mph", "utc", null));
            await preferences.SetAsync(alternateIssuer, subject, new("m", "m/s", "browser", null));
        }

        using var root = RealPreferencesFactory(issuer, subject);
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(ConfigureStorage));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        Assert.Equal(new DisplayPreferenceDTO("ft", "mph", "utc", null),
            await client.GetFromJsonAsync<DisplayPreferenceDTO>(DisplayRoute));
        root.Principal = ProfilesApiFactory.Identity(alternateIssuer, subject);
        Assert.Equal(new DisplayPreferenceDTO("m", "m/s", "browser", null),
            await client.GetFromJsonAsync<DisplayPreferenceDTO>(DisplayRoute));
        root.Principal = ProfilesApiFactory.Identity(issuer, subject);
        Assert.Equal(new DisplayPreferenceDTO("ft", "mph", "utc", null),
            await client.GetFromJsonAsync<DisplayPreferenceDTO>(DisplayRoute));

        Assert.Equal(3, root.Cache.Reads.Count);
        Assert.Equal(2, root.Cache.Reads.Select(read => read.Key).Distinct().Count());
    }

    [PostgresFact]
    public async Task SuccessfulDisplayWrite_InvalidatesIdentityKeyAndNextReadReloads()
    {
        using var root = RealPreferencesFactory("display-write-" + Guid.NewGuid(), Guid.NewGuid().ToString());
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(ConfigureStorage));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(new DisplayPreferenceDTO("m", "km/h", "browser", null),
            await client.GetFromJsonAsync<DisplayPreferenceDTO>(DisplayRoute));

        var updated = new UpdateDisplayPreferenceRequest("ft", "mph", "utc", null);
        using var write = await client.PutAsJsonAsync(DisplayRoute, updated);
        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        Assert.Equal(new DisplayPreferenceDTO("ft", "mph", "utc", null),
            await write.Content.ReadFromJsonAsync<DisplayPreferenceDTO>());
        Assert.Single(root.Cache.Removals);
        Assert.Equal(root.Cache.Reads[0].Key, root.Cache.Removals[0].Key);
        Assert.True(root.Cache.Removals[0].Token.CanBeCanceled);

        Assert.Equal(new DisplayPreferenceDTO("ft", "mph", "utc", null),
            await client.GetFromJsonAsync<DisplayPreferenceDTO>(DisplayRoute));
        Assert.Equal(2, root.Cache.Reads.Count);
    }

    [PostgresFact]
    public async Task FailedDisplayWrite_DoesNotEvictWarmRead()
    {
        using var root = RealPreferencesFactory("display-failed-write-" + Guid.NewGuid(), Guid.NewGuid().ToString());
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(ConfigureStorage));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var initial = new DisplayPreferenceDTO("m", "km/h", "browser", null);
        Assert.Equal(initial, await client.GetFromJsonAsync<DisplayPreferenceDTO>(DisplayRoute));

        using var write = await client.PutAsJsonAsync(
            DisplayRoute, new UpdateDisplayPreferenceRequest("unsupported", "mph", "utc", null));
        Assert.Equal(HttpStatusCode.BadRequest, write.StatusCode);
        Assert.Empty(root.Cache.Removals);
        Assert.Equal(initial, await client.GetFromJsonAsync<DisplayPreferenceDTO>(DisplayRoute));
        Assert.Equal(2, root.Cache.Reads.Count);
    }

    [PostgresFact]
    public async Task DisplayInvalidationFailure_AfterSuccessfulWrite_IsVisible()
    {
        var issuer = "display-invalidation-failure-" + Guid.NewGuid();
        var subject = Guid.NewGuid().ToString();
        using var root = RealPreferencesFactory(issuer, subject);
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(ConfigureStorage));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        await client.GetAsync(DisplayRoute);
        var failure = new InvalidOperationException("cache unavailable");
        root.Cache.RemoveFailure = failure;

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PutAsJsonAsync(DisplayRoute, new UpdateDisplayPreferenceRequest("ft", "mph", "utc", null)));

        Assert.Same(failure, actual);
        Assert.Single(root.Cache.Removals);
        await using var verify = database.CreateContext();
        Assert.Equal(new DisplayPreferenceDTO("ft", "mph", "utc", null),
            await new DisplayPreferenceService(verify).GetAsync(issuer, subject));
    }

    private static ProfilesApiFactory RealPreferencesFactory(string issuer, string subject) => new()
    {
        UseRealPreferences = true,
        Principal = ProfilesApiFactory.Identity(issuer, subject)
    };

    private void ConfigureStorage(IServiceCollection services)
    {
        var types = new[]
        {
            typeof(ProfilesDbContext),
            typeof(DbContextOptions<ProfilesDbContext>),
            typeof(IDbContextOptionsConfiguration<ProfilesDbContext>)
        };
        foreach (var registration in services.Where(service => types.Contains(service.ServiceType)).ToArray())
            services.Remove(registration);
        services.AddDbContext<ProfilesDbContext>(options => options
            .UseNpgsql(database.ConnectionString,
                postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "ef"))
            .UseSnakeCaseNamingConvention());
    }
}
