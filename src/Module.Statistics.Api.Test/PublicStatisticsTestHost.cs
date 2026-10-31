using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VeloTime.Module.Statistics.Api.Endpoints;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

/// <summary>
/// A new, synthetic database per test. The supplied PostgreSQL connection is used only
/// to CREATE/DROP that database; no existing database/schema is reset.
/// </summary>
internal sealed class PublicStatisticsTestHost : IAsyncDisposable
{
    public const string PostgreSqlEnvironment = "VELOTIMER_PUBLIC_STATISTICS_POSTGRES";
    public static readonly Guid Facility = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid OtherFacility = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid Start = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid End = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public readonly PointDependencies Points = new();
    public readonly SqlCapture Sql = new();
    private StatisticsApiFactory root = null!;
    public WebApplicationFactory<Program> App { get; private set; } = null!;
    private string? adminConnection;
    private string? database;

    public static async Task<PublicStatisticsTestHost> CreateAsync(bool postgres = false)
    {
        var host = new PublicStatisticsTestHost();
        try
        {
            string? connection = null;
            if (postgres)
            {
                host.adminConnection = Environment.GetEnvironmentVariable(PostgreSqlEnvironment)
                    ?? throw new InvalidOperationException($"Set {PostgreSqlEnvironment} to an isolated test PostgreSQL server.");
                host.database = "velotimer_public_tests_" + Guid.NewGuid().ToString("N");
                await using var admin = new NpgsqlConnection(host.adminConnection);
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{host.database}\"", admin);
                await create.ExecuteNonQueryAsync();
                var settings = new NpgsqlConnectionStringBuilder(host.adminConnection) { Database = host.database };
                connection = settings.ConnectionString;
                await using var isolated = new NpgsqlConnection(connection);
                await isolated.OpenAsync();
                await using var extension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS pg_trgm", isolated);
                await extension.ExecuteNonQueryAsync();
            }
            host.Points.Facilities[Start] = Facility;
            host.Points.Facilities[End] = Facility;
            host.Points.Names[Start] = "Start";
            host.Points.Names[End] = "Finish";
            host.root = new StatisticsApiFactory { PostgreSqlConnectionString = connection };
            host.App = host.root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                foreach (var descriptor in services.Where(service =>
                    service.ServiceType == typeof(ICoursePointReferences) ||
                    service.ServiceType == typeof(IPublicCoursePointNames)).ToArray())
                    services.Remove(descriptor);
                services.AddSingleton<ICoursePointReferences>(host.Points);
                services.AddSingleton<IPublicCoursePointNames>(host.Points);
                services.AddDbContext<StatisticsDbContext>(options => options.AddInterceptors(host.Sql));
            }));
            await using var scope = host.App.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>().Database.EnsureCreatedAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    public async Task SeedAsync(params object[] entities)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        storage.AddRange(entities);
        await storage.SaveChangesAsync();
    }

    public async Task MutateAsync(Func<StatisticsDbContext, Task> mutation)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        await mutation(storage);
        await storage.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null) await App.DisposeAsync();
        if (root is not null) await root.DisposeAsync();
        if (database is not null && adminConnection is not null)
        {
            // Clear only this test database's pool before dropping it.
            var settings = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database };
            using var poolConnection = new NpgsqlConnection(settings.ConnectionString);
            NpgsqlConnection.ClearPool(poolConnection);
            await using var admin = new NpgsqlConnection(adminConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    internal sealed class PointDependencies : ICoursePointReferences, IPublicCoursePointNames
    {
        public Dictionary<Guid, Guid?> Facilities { get; } = [];
        public Dictionary<Guid, string?> Names { get; } = [];
        public ConcurrentBag<Guid> FacilityCalls { get; } = [];
        public ConcurrentBag<Guid> NameCalls { get; } = [];
        public Exception? Failure { get; set; }
        public Dictionary<Guid, Exception> FacilityFailures { get; } = [];
        public Exception? NameFailure { get; set; }
        public Task<Guid?> GetFacilityAsync(Guid pointId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FacilityCalls.Add(pointId);
            if (Failure is not null) throw Failure;
            if (FacilityFailures.TryGetValue(pointId, out var failure)) throw failure;
            return Task.FromResult(Facilities.GetValueOrDefault(pointId));
        }
        public Task<string?> GetNameAsync(Guid pointId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NameCalls.Add(pointId);
            if (Failure is not null) throw Failure;
            if (NameFailure is not null) throw NameFailure;
            return Task.FromResult(Names.GetValueOrDefault(pointId));
        }
    }

    internal sealed class SqlCapture : DbCommandInterceptor
    {
        public ConcurrentQueue<(string Text, object?[] Parameters)> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue((command.CommandText,
                command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray()));
            return ValueTask.FromResult(result);
        }
    }
}

public sealed class PublicStatisticsPostgreSqlFactAttribute : FactAttribute
{
    public PublicStatisticsPostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PublicStatisticsTestHost.PostgreSqlEnvironment)))
            Skip = $"Requires {PublicStatisticsTestHost.PostgreSqlEnvironment}; PostgreSQL-specific behavior is not validated by SQLite.";
    }
}
