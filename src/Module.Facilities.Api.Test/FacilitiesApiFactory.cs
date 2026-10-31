using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Common;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class FacilitiesApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public string? SqliteFilePath { get; init; }
    public TestReferences References { get; } = new();
    public RecordingOutputCacheStore OutputCache { get; } = new();
    public bool UseProductionRedisCache { get; init; }
    public string? RedisConnectionString { get; init; }
    // Test-only hook applied after production registrations (for decorating providers beneath FusionCache).
    public Action<IServiceCollection>? ConfigureTestServices { get; init; }
    private string? postgresAdminConnectionString;
    private string? postgresTestConnectionString;
    private string? postgresTestDatabase;
    private bool postgresSchemaCreated;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (SqliteFilePath is null) connection.Open();
        builder.UseEnvironment("Development");
        var redis = RedisConnectionString ?? Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS");
        if (UseProductionRedisCache)
        {
            if (string.IsNullOrWhiteSpace(redis))
                throw new InvalidOperationException("VELOTIME_TEST_REDIS is required for the Redis cache fixture.");
            builder.UseSetting("ConnectionStrings:cache", redis);
        }
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:velotimedb"] = "Host=localhost;Database=facilities-tests;Username=test;Password=test",
            ["ConnectionStrings:cache"] = UseProductionRedisCache ? redis : "localhost:6379",
            ["Aspire:StackExchange:Redis:ConnectionString"] = UseProductionRedisCache ? redis : null
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<FacilityDbContext>();
            services.RemoveAll<DbContextOptions<FacilityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FacilityDbContext>>();
            services.AddDbContext<FacilityDbContext>(options =>
            {
                if (SqliteFilePath is { } file) options.UseSqlite($"Data Source={file}");
                else if (postgresTestConnectionString is null) options.UseSqlite(connection);
                else options.UseNpgsql(postgresTestConnectionString);
            });

            services.RemoveAll<IOutputCacheStore>();
            services.AddSingleton<IOutputCacheStore>(OutputCache);
            services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, TestSetCookieStartupFilter>();
            if (!UseProductionRedisCache)
            {
                services.RemoveAll<HybridCache>();
                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();
                services.AddFusionCache()
                    .WithSerializer(new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions
                    {
                        ReferenceHandler = ReferenceHandler.Preserve
                    }))
                    .AsHybridCache();
            }
            services.AddSingleton<IAdministrationReferences>(References);
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            ConfigureTestServices?.Invoke(services);
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        References.Dependencies = 0;
        References.Unavailable = false;
        References.FacilityId = Guid.Empty;
        var postgres = Environment.GetEnvironmentVariable("VELOTIME_TEST_POSTGRES");
        if (SqliteFilePath is null && !string.IsNullOrWhiteSpace(postgres) && postgresTestDatabase is null)
        {
            postgresAdminConnectionString = postgres;
            postgresTestDatabase = $"vt_facilities_api_{Guid.NewGuid():N}";
            await using var admin = new NpgsqlConnection(postgresAdminConnectionString);
            await admin.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{postgresTestDatabase}\"", admin))
                await create.ExecuteNonQueryAsync();
            postgresTestConnectionString = new NpgsqlConnectionStringBuilder(postgresAdminConnectionString)
            {
                Database = postgresTestDatabase
            }.ConnectionString;
        }

        await using var scope = Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        if (SqliteFilePath is not null || postgresTestConnectionString is null)
        {
            await storage.Database.EnsureDeletedAsync();
            await storage.Database.EnsureCreatedAsync();
        }
        else if (!postgresSchemaCreated)
        {
            await storage.Database.EnsureCreatedAsync();
            postgresSchemaCreated = true;
        }
        else
        {
            await TruncateOwnedSchemaAsync(storage, "facility");
        }
    }

    private static async Task TruncateOwnedSchemaAsync(FacilityDbContext storage, string schema)
    {
        await storage.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)storage.Database.GetDbConnection();
        var tableNames = new List<string>();
        await using (var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema AND table_type = 'BASE TABLE' AND table_name <> '__EFMigrationsHistory'",
            connection))
        {
            command.Parameters.AddWithValue("schema", schema);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tableNames.Add(reader.GetString(0));
        }

        if (tableNames.Count > 0)
        {
            var qualifiedNames = tableNames.Select(name =>
                $"{QuoteIdentifier(schema)}.{QuoteIdentifier(name)}");
            await using var truncate = new NpgsqlCommand(
                $"TRUNCATE TABLE {string.Join(", ", qualifiedNames)} RESTART IDENTITY CASCADE",
                connection);
            await truncate.ExecuteNonQueryAsync();
        }
        await storage.Database.CloseConnectionAsync();
    }

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

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
            if (postgresTestDatabase is not null && postgresTestConnectionString is not null && postgresAdminConnectionString is not null)
            {
                using var pooledConnection = new NpgsqlConnection(postgresTestConnectionString);
                NpgsqlConnection.ClearPool(pooledConnection);
                using var admin = new NpgsqlConnection(postgresAdminConnectionString);
                admin.Open();
                using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{postgresTestDatabase}\" WITH (FORCE)", admin);
                drop.ExecuteNonQuery();
            }
        }
    }
}
