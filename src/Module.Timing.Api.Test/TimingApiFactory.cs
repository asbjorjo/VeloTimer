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
using Microsoft.Extensions.Hosting;
using SlimMessageBus;
using Npgsql;
using System.Text.Json;
using System.Text.Json.Serialization;
using VeloTime.Module.Timing.Storage;
using VeloTime.Module.Common;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace VeloTime.Module.Timing.Api.Test;

public sealed class TimingApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public TestReferences References { get; } = new();
    public RecordingOwnershipMessageBus Messages { get; } = RecordingOwnershipMessageBus.Create();
    public TestClock Clock { get; } = new();
    private string? postgresAdminConnectionString;
    private string? postgresTestConnectionString;
    private string? postgresTestDatabase;
    private bool postgresSchemaCreated;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:MessageBus", "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:velotimedb"] = "Host=localhost;Database=timing-tests;Username=test;Password=test",
            ["ConnectionStrings:cache"] = "localhost:6379"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<TimingDbContext>();
            services.RemoveAll<DbContextOptions<TimingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<TimingDbContext>>();
            services.AddDbContext<TimingDbContext>(options =>
            {
                if (postgresTestConnectionString is null) options.UseSqlite(connection);
                else options.UseNpgsql(postgresTestConnectionString);
            });

            foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                && (descriptor.ImplementationType?.Namespace?.StartsWith("SlimMessageBus", StringComparison.Ordinal) == true
                    || descriptor.ImplementationType?.Name == "OwnershipOutboxStartup")).ToList())
                services.Remove(descriptor);
            services.RemoveAll<IMessageBus>();
            services.AddSingleton<IMessageBus>(Messages.Bus);

            services.RemoveAll<HybridCache>();
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
            services.AddFusionCache()
                .WithSerializer(new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions
                {
                    ReferenceHandler = ReferenceHandler.Preserve
                }))
                .AsHybridCache();
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
        Messages.Reset();
        var postgres = Environment.GetEnvironmentVariable("VELOTIME_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(postgres) && postgresTestDatabase is null)
        {
            postgresAdminConnectionString = postgres;
            postgresTestDatabase = $"vt_timing_api_{Guid.NewGuid():N}";
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
        var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        if (postgresTestConnectionString is null)
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
            await TruncateOwnedSchemaAsync(storage, "timing");
        }
    }

    private static async Task TruncateOwnedSchemaAsync(TimingDbContext storage, string schema)
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
        public Task<EntityReference?> GetAsync(string module, string kind, Guid id, CancellationToken cancellationToken) =>
            Unavailable ? throw new ReferenceUnavailableException() : Task.FromResult<EntityReference?>(new(id, id));
        public Task<DependencySummary> GetDependenciesAsync(string module, string kind, Guid id, CancellationToken cancellationToken) =>
            Unavailable ? throw new ReferenceUnavailableException() : Task.FromResult(new DependencySummary(Dependencies, "Coursepoints are still linked to this timing point."));
    }

    public sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
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
