using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Statistics.Api.Endpoints;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class StatisticsApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public string? PostgreSqlConnectionString { get; init; }
    private string? postgresAdminConnectionString;
    private string? ownedPostgresConnectionString;
    private string? ownedPostgresDatabase;
    private bool postgresSchemaCreated;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Open SQLite unless InitializeDatabaseAsync already provisioned an isolated PostgreSQL database.
        if (PostgreSqlConnectionString is null && ownedPostgresConnectionString is null)
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
            var postgresConnectionString = PostgreSqlConnectionString ?? ownedPostgresConnectionString;
            if (postgresConnectionString is { } postgres)
                services.AddDbContext<StatisticsDbContext>(options => options.UseNpgsql(postgres).UseSnakeCaseNamingConvention());
            else
            {
                services.AddDbContext<StatisticsDbContext>(options => options.UseSqlite(connection));
                services.AddScoped<StatisticsDbContext>(provider =>
                    new TestStatisticsDbContext(provider.GetRequiredService<DbContextOptions<StatisticsDbContext>>()));
            }

            RemoveServices<HybridCache>(services);
            RemoveServices<IDistributedCache>(services);
            services.AddDistributedMemoryCache();
            services.AddFusionCache()
                .WithSerializer(new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions
                {
                    ReferenceHandler = ReferenceHandler.Preserve
                }))
                .AsHybridCache();
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
        var postgres = Environment.GetEnvironmentVariable("VELOTIME_TEST_POSTGRES");
        if (PostgreSqlConnectionString is null && !string.IsNullOrWhiteSpace(postgres) && ownedPostgresDatabase is null)
        {
            postgresAdminConnectionString = postgres;
            ownedPostgresDatabase = $"vt_statistics_api_{Guid.NewGuid():N}";
            await using var admin = new NpgsqlConnection(postgresAdminConnectionString);
            await admin.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{ownedPostgresDatabase}\"", admin))
                await create.ExecuteNonQueryAsync();
            ownedPostgresConnectionString = new NpgsqlConnectionStringBuilder(postgresAdminConnectionString)
            {
                Database = ownedPostgresDatabase
            }.ConnectionString;
        }

        await using var scope = Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        if (PostgreSqlConnectionString is not null || ownedPostgresConnectionString is not null)
        {
            if (!postgresSchemaCreated)
            {
                await storage.Database.EnsureCreatedAsync();
                postgresSchemaCreated = true;
            }
            else
            {
                await TruncateOwnedSchemaAsync(storage, "statistics");
            }
        }
        else
        {
            await storage.Database.EnsureDeletedAsync();
            await storage.Database.EnsureCreatedAsync();
        }
    }

    private static async Task TruncateOwnedSchemaAsync(StatisticsDbContext storage, string schema)
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
            // The production partial trigram index uses PostgreSQL's snake-case column.
            // This existing SQLite discovery fixture has CLR column names; PostgreSQL
            // result/index semantics are exercised separately against the real provider.
            modelBuilder.Entity<Model.StatsProfile>().HasIndex(profile => profile.ProfileName)
                .HasFilter("\"IsPublic\" = 1");
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            connection.Dispose();
            if (ownedPostgresDatabase is not null && ownedPostgresConnectionString is not null && postgresAdminConnectionString is not null)
            {
                using var pooledConnection = new NpgsqlConnection(ownedPostgresConnectionString);
                NpgsqlConnection.ClearPool(pooledConnection);
                using var admin = new NpgsqlConnection(postgresAdminConnectionString);
                admin.Open();
                using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{ownedPostgresDatabase}\" WITH (FORCE)", admin);
                drop.ExecuteNonQuery();
            }
        }
    }
}
