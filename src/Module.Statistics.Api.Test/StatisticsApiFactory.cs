using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class StatisticsApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:velotimedb"] = "Host=localhost;Database=statistics-tests;Username=test;Password=test",
            ["ConnectionStrings:cache"] = "localhost:6379"
        }));
        builder.ConfigureServices(services =>
        {
            RemoveServices<StatisticsDbContext>(services);
            RemoveServices<DbContextOptions<StatisticsDbContext>>(services);
            RemoveServices<IDbContextOptionsConfiguration<StatisticsDbContext>>(services);
            services.AddDbContext<StatisticsDbContext>(options => options.UseSqlite(connection));

            RemoveServices<HybridCache>(services);
            services.AddHybridCache();
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.AuthenticationSchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.AuthenticationSchemeName;
                options.DefaultScheme = TestAuthenticationHandler.AuthenticationSchemeName;
            }).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.AuthenticationSchemeName,
                _ => { });
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        await storage.Database.EnsureDeletedAsync();
        await storage.Database.EnsureCreatedAsync();
    }

    private static void RemoveServices<TService>(IServiceCollection services)
    {
        foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(TService)).ToList())
        {
            services.Remove(descriptor);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            connection.Dispose();
        }
    }
}
