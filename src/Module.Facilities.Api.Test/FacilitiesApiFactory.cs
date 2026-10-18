using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class FacilitiesApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:velotimedb"] = "Host=localhost;Database=facilities-tests;Username=test;Password=test",
            ["ConnectionStrings:cache"] = "localhost:6379"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<FacilityDbContext>();
            services.RemoveAll<DbContextOptions<FacilityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FacilityDbContext>>();
            services.AddDbContext<FacilityDbContext>(options => options.UseSqlite(connection));

            services.RemoveAll<HybridCache>();
            services.AddHybridCache();
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        await storage.Database.EnsureDeletedAsync();
        await storage.Database.EnsureCreatedAsync();
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
