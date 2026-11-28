using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;
using Assert = Xunit.Assert;

namespace VeloTime.Module.Timing.Api.Test;

/// <summary>
/// Real PostgreSQL checks for loop status storage: the migrated schema, the least/greatest rollup upsert, and that
/// concurrent, shuffled and replayed deliveries give one raw row per sample and the same rollup.
/// </summary>
public sealed class LoopStatusPostgreSqlTests : IAsyncLifetime
{
    private readonly string databaseName = $"loop_status_{Guid.NewGuid():N}";
    private string connectionString = "";
    private string adminConnectionString = "";
    private ServiceProvider? services;

    public async Task InitializeAsync()
    {
        adminConnectionString = Environment.GetEnvironmentVariable("VELOTIME_TEST_OUTBOX_POSTGRES") ?? "";
        if (string.IsNullOrWhiteSpace(adminConnectionString)) return;
        await ExecuteSql(adminConnectionString, $"CREATE DATABASE \"{databaseName}\"");
        connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
        services = new ServiceCollection()
            .AddDbContext<TimingDbContext>(options => options.UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "ef")).UseSnakeCaseNamingConvention())
            .AddHybridCache().Services
            .AddScoped<InstallationService>()
            .AddScoped<LoopStatusService>()
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TimingDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (services is not null) await services.DisposeAsync();
        if (!string.IsNullOrWhiteSpace(connectionString))
            await ExecuteSql(adminConnectionString, $"DROP DATABASE \"{databaseName}\" WITH (FORCE)");
    }

    [PostgreSqlOutboxFact]
    public async Task ConcurrentShuffledAndReplayedSamplesGiveOneRowEachAndTheExpectedRollup()
    {
        var start = new DateTime(2026, 11, 28, 10, 0, 0, DateTimeKind.Utc);
        var samples = Enumerable.Range(0, 120).Select(i => new LoopStatusObservation("pg-agent", "finish", start.AddSeconds(5 * i),
            Noise: 10 + i % 7, DecoderTemperature: i % 3 == 0 ? null : 300 + i % 5, InputVoltage: 12,
            LoopTriggers: i % 2, TimeSynchronised: i % 10 != 0, NoiseSharesStrengthScale: i % 2 == 0)).ToArray();
        // Resolve installation and timing point once so concurrent deliveries race only on the samples.
        await Record(samples[0]);

        var deliveries = samples.Concat(samples.Reverse()).OrderBy(s => s.Time.Ticks * 7919 % 1009).ToArray();
        await Parallel.ForEachAsync(deliveries, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (sample, _) => await Record(sample));

        Assert.Equal(120, await Scalar("SELECT count(*)::int FROM timing.loop_status"));
        Assert.Equal(2, await Scalar("SELECT count(*)::int FROM timing.loop_status_rollup"));
        foreach (var bucket in samples.GroupBy(s => LoopStatusRollup.BucketOf(s.Time)))
        {
            var temperatures = bucket.Where(s => s.DecoderTemperature is not null).Select(s => s.DecoderTemperature!.Value).ToArray();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT samples, noise_sum, noise_min, noise_max, temperature_samples, temperature_sum, temperature_min, temperature_max, " +
                "triggers_sum, sync_samples, unsynchronised_samples, shared_scale_samples FROM timing.loop_status_rollup WHERE bucket_start = @b", connection);
            command.Parameters.AddWithValue("b", bucket.Key);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(bucket.Count(), reader.GetInt32(0));
            Assert.Equal(bucket.Sum(s => s.Noise), reader.GetDouble(1), 6);
            Assert.Equal(bucket.Min(s => s.Noise), reader.GetDouble(2));
            Assert.Equal(bucket.Max(s => s.Noise), reader.GetDouble(3));
            Assert.Equal(temperatures.Length, reader.GetInt32(4));
            Assert.Equal(temperatures.Sum(), reader.GetDouble(5), 6);
            Assert.Equal(temperatures.Min(), reader.GetDouble(6));
            Assert.Equal(temperatures.Max(), reader.GetDouble(7));
            Assert.Equal(bucket.Sum(s => s.LoopTriggers ?? 0), reader.GetInt64(8));
            Assert.Equal(bucket.Count(), reader.GetInt32(9));
            Assert.Equal(bucket.Count(s => s.TimeSynchronised == false), reader.GetInt32(10));
            Assert.Equal(bucket.Count(s => s.NoiseSharesStrengthScale == true), reader.GetInt32(11));
        }
    }

    [PostgreSqlOutboxFact]
    public async Task RollupKeepsMinAndMaxWhenLaterSamplesOmitAValue()
    {
        var time = new DateTime(2026, 11, 28, 10, 0, 0, DateTimeKind.Utc);
        await Record(new LoopStatusObservation("pg-agent", "finish", time, 5, InputVoltage: 12.5));
        await Record(new LoopStatusObservation("pg-agent", "finish", time.AddSeconds(5), 7));
        await Record(new LoopStatusObservation("pg-agent", "finish", time.AddSeconds(10), 3, InputVoltage: 11.5));

        Assert.Equal(1, await Scalar(
            "SELECT count(*)::int FROM timing.loop_status_rollup WHERE samples = 3 AND noise_min = 3 AND noise_max = 7 " +
            "AND voltage_samples = 2 AND voltage_min = 11.5 AND voltage_max = 12.5 AND temperature_min IS NULL AND sync_samples = 0"));
    }

    private async Task Record(LoopStatusObservation observation)
    {
        await using var scope = services!.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LoopStatusService>().RecordAsync(observation);
    }

    private async Task<int> Scalar(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteSql(string connection, string sql)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        await command.ExecuteNonQueryAsync();
    }
}
