using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.Module.Profiles.Storage;

namespace VeloTime.Module.Profiles.Api.Test;

[Trait("Category", "PostgreSQL")]
public sealed class PostgresApiTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [PostgresFact]
    public async Task RealApiServiceAndPostgres_PersistOnlyValidSelfWrites_AcrossHosts()
    {
        var subject = Guid.NewGuid().ToString();
        using (var root = new ProfilesApiFactory
        {
            UseRealPreferences = true,
            Principal = ProfilesApiFactory.Identity("https://issuer.example/A", subject)
        })
        using (var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(ConfigureStorage)))
        using (var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") }))
        {
            using var missing = await client.GetAsync("/api/profiles/me/preferences");
            Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
            Assert.Null((await missing.Content.ReadFromJsonAsync<LanguagePreferenceDTO>())!.Language);
            await using var verify = database.CreateContext();
            Assert.False(await verify.LanguagePreferences.AnyAsync(p => p.Subject == subject));

            using var invalid = await client.PutAsync("/api/profiles/me/preferences/language",
                new StringContent("""{"language":"no"}""", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.False(await verify.LanguagePreferences.AnyAsync(p => p.Subject == subject));

            using var saved = await client.PutAsync(
                "/api/profiles/me/preferences/language?issuer=https://issuer.example/B&subject=victim",
                new StringContent($$"""{"language":"nb-NO","issuer":"https://issuer.example/B","subject":"victim"}""",
                    Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal("nb-NO", (await saved.Content.ReadFromJsonAsync<LanguagePreferenceDTO>())!.Language);
            var row = Assert.Single(await verify.LanguagePreferences.Where(p => p.Subject == subject).ToListAsync());
            Assert.Equal("https://issuer.example/A", row.Issuer);
            Assert.Equal("nb-NO", row.Language);

            root.Principal = ProfilesApiFactory.Identity("https://issuer.example/B", subject);
            using var otherIssuer = await client.GetAsync("/api/profiles/me/preferences");
            Assert.Equal(HttpStatusCode.OK, otherIssuer.StatusCode);
            Assert.Null((await otherIssuer.Content.ReadFromJsonAsync<LanguagePreferenceDTO>())!.Language);
            root.Principal = ProfilesApiFactory.Identity("https://issuer.example/A", subject + "-other");
            using var otherSubject = await client.GetAsync("/api/profiles/me/preferences");
            Assert.Equal(HttpStatusCode.OK, otherSubject.StatusCode);
            Assert.Null((await otherSubject.Content.ReadFromJsonAsync<LanguagePreferenceDTO>())!.Language);
            Assert.Equal(1, await verify.LanguagePreferences.CountAsync(p => p.Subject == subject));
        }

        using var restartedRoot = new ProfilesApiFactory
        {
            UseRealPreferences = true,
            Principal = ProfilesApiFactory.Identity("https://issuer.example/A", subject)
        };
        using var restarted = restartedRoot.WithWebHostBuilder(builder => builder.ConfigureTestServices(ConfigureStorage));
        using var restartedClient = restarted.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var persisted = await restartedClient.GetAsync("/api/profiles/me/preferences");
        Assert.Equal(HttpStatusCode.OK, persisted.StatusCode);
        Assert.Equal("nb-NO", (await persisted.Content.ReadFromJsonAsync<LanguagePreferenceDTO>())!.Language);
        Assert.Empty(restartedRoot.Preferences.Calls); // The recording double was never used.
    }

    private void ConfigureStorage(IServiceCollection services)
    {
        var types = new[]
        {
            typeof(ProfilesDbContext), typeof(DbContextOptions<ProfilesDbContext>),
            typeof(IDbContextOptionsConfiguration<ProfilesDbContext>)
        };
        foreach (var registration in services.Where(s => types.Contains(s.ServiceType)).ToArray())
            services.Remove(registration);
        services.AddDbContext<ProfilesDbContext>(options => options
            .UseNpgsql(database.ConnectionString, pg => pg.MigrationsHistoryTable("__ef_migrations_history", "ef"))
            .UseSnakeCaseNamingConvention());
    }
}
