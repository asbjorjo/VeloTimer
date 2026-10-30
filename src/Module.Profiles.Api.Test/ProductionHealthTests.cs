using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Profiles.Storage;

namespace VeloTime.Module.Profiles.Api.Test;

public sealed class ProductionHealthTests
{
    [Fact]
    public async Task UnavailableDatabase_IsNotReady_ButProcessIsAlive_WithoutAuthentication()
    {
        using var factory = new ProfilesApiFactory { Principal = null };
        using var client = factory.CreateContractClient();
        using var readiness = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal("Unhealthy", await readiness.Content.ReadAsStringAsync());
        using var liveness = await client.GetAsync("/alive");
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        Assert.Equal("Healthy", await liveness.Content.ReadAsStringAsync());
        Assert.Empty(factory.Preferences.Calls);
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task MissingSchema_IsNotReady_ThenMigrationMakesReady_WithoutCreatingPreferences()
    {
        var database = new PostgresFixture { ApplyMigrations = false };
        await database.InitializeAsync();
        try
        {
            using var root = new ProfilesApiFactory { Principal = null };
            using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                var replaced = new[]
                {
                    typeof(ProfilesDbContext), typeof(DbContextOptions<ProfilesDbContext>),
                    typeof(IDbContextOptionsConfiguration<ProfilesDbContext>)
                };
                foreach (var registration in services.Where(s => replaced.Contains(s.ServiceType)).ToArray())
                    services.Remove(registration);
                services.AddDbContext<ProfilesDbContext>(options => options
                    .UseNpgsql(database.ConnectionString, pg => pg.MigrationsHistoryTable("__ef_migrations_history", "ef"))
                    .UseSnakeCaseNamingConvention());
            }));
            using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            using var missing = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, missing.StatusCode);
            Assert.Equal("Unhealthy", await missing.Content.ReadAsStringAsync());
            using var alive = await client.GetAsync("/alive");
            Assert.Equal(HttpStatusCode.OK, alive.StatusCode);

            await using var context = database.CreateContext();
            await context.Database.MigrateAsync();
            using var ready = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
            Assert.False(await context.LanguagePreferences.AnyAsync());
            Assert.Empty(root.Preferences.Calls);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }
}
