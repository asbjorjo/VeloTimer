using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VeloTime.Module.Timing.Storage;
using VeloTime.Module.Common;

namespace VeloTime.Module.Timing.Api.Test;

public sealed class TimingApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public TestReferences References { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:velotimedb"] = "Host=localhost;Database=timing-tests;Username=test;Password=test",
            ["ConnectionStrings:cache"] = "localhost:6379"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimingDbContext>();
            services.RemoveAll<DbContextOptions<TimingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<TimingDbContext>>();
            var postgres = Environment.GetEnvironmentVariable("VELOTIME_TEST_POSTGRES");
            services.AddDbContext<TimingDbContext>(options =>
            {
                if (string.IsNullOrWhiteSpace(postgres)) options.UseSqlite(connection);
                else options.UseNpgsql(postgres);
            });

            services.RemoveAll<HybridCache>();
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
            services.AddHybridCache();
            services.AddSingleton<IAdministrationReferences>(References);
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        References.Dependencies = 0;
        References.Unavailable = false;
        await using var scope = Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        await storage.Database.EnsureDeletedAsync();
        await storage.Database.EnsureCreatedAsync();
    }

    public sealed class TestReferences : IAdministrationReferences
    {
        public int Dependencies { get; set; }
        public bool Unavailable { get; set; }
        public Task<EntityReference?> GetAsync(string module, string kind, Guid id, CancellationToken cancellationToken) =>
            Unavailable ? throw new ReferenceUnavailableException() : Task.FromResult<EntityReference?>(new(id, id));
        public Task<DependencySummary> GetDependenciesAsync(string module, string kind, Guid id, CancellationToken cancellationToken) =>
            Unavailable ? throw new ReferenceUnavailableException() : Task.FromResult(new DependencySummary(Dependencies, "Coursepoints are still linked to this timing point."));
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
