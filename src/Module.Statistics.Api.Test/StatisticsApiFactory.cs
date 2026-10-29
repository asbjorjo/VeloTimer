using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Statistics.Api.Endpoints;

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
            services.AddScoped<StatisticsDbContext>(provider =>
                new TestStatisticsDbContext(provider.GetRequiredService<DbContextOptions<StatisticsDbContext>>()));

            RemoveServices<HybridCache>(services);
            RemoveServices<IDistributedCache>(services);
            services.AddDistributedMemoryCache();
            services.AddHybridCache();
            services.AddSingleton<ICoursePointReferences, TestCoursePointReferences>();
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

    private sealed class TestCoursePointReferences : ICoursePointReferences
    {
        public Task<Guid?> GetFacilityAsync(Guid pointId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(pointId == Guid.Empty ? null : Guid.Parse("00000000-0000-0000-0000-000000000001"));
    }

    private sealed class TestStatisticsDbContext(DbContextOptions<StatisticsDbContext> options) : StatisticsDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // SQLite cannot order TimeSpan values natively.
            modelBuilder.Entity<Model.StatisticsEntry>().Property(entry => entry.Duration).HasConversion<long>();
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
