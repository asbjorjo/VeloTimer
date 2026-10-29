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
using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Common;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class FacilitiesApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public TestReferences References { get; } = new();

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
            var postgres = Environment.GetEnvironmentVariable("VELOTIME_TEST_POSTGRES");
            services.AddDbContext<FacilityDbContext>(options =>
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
        References.FacilityId = Guid.Empty;
        await using var scope = Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        await storage.Database.EnsureDeletedAsync();
        await storage.Database.EnsureCreatedAsync();
    }

    public sealed class TestReferences : IAdministrationReferences
    {
        public int Dependencies { get; set; }
        public bool Unavailable { get; set; }
        public Guid FacilityId { get; set; }
        public Task<EntityReference?> GetAsync(string module, string kind, Guid id, CancellationToken cancellationToken) =>
            Unavailable ? throw new ReferenceUnavailableException() : Task.FromResult<EntityReference?>(new(id, FacilityId));
        public Task<DependencySummary> GetDependenciesAsync(string module, string kind, Guid id, CancellationToken cancellationToken) =>
            Unavailable ? throw new ReferenceUnavailableException() : Task.FromResult(new DependencySummary(Dependencies, "Related timing/statistics records must be removed first."));
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
