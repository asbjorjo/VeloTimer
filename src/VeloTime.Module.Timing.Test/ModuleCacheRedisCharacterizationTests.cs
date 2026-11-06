using System.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using System.Runtime.InteropServices;
using StackExchange.Redis;
using Xunit.Abstractions;
using VeloTime.Module.Common;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing.Test;

public sealed class ModuleCacheRedisCharacterizationTests(ITestOutputHelper output)
{
    private const int RunCount = 24;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);
    // Four 96-run passes measured a maximum of 18.65ms; 250ms is a fixed >13x
    // margin for scheduling/CI variance, not a production freshness SLA.
    private static readonly TimeSpan RegressionDeadline = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(3);

    [RedisAndPostgresFact]
    public async Task Production_module_cache_propagates_exact_key_and_tag_evictions_to_a_warm_host()
    {
        var redis = RequiredEnvironmentVariable("VELOTIME_TEST_REDIS");
        var postgres = RequiredEnvironmentVariable("VELOTIME_TEST_POSTGRES");
        var cacheNamespace = $"velotimer:phase-one:{Guid.NewGuid():N}:";

        await using var database = await CacheDatabase.CreateAsync(postgres);
        await using var hostA = await CacheHost.CreateAsync(redis, postgres);
        await using var hostB = await CacheHost.CreateAsync(redis, postgres);

        output.WriteLine($"Environment: {RuntimeInformation.FrameworkDescription}; {Environment.OSVersion}; PostgreSQL {database.ServerVersion}; independent Generic Hosts/scopes; isolated Redis endpoint configured by VELOTIME_TEST_REDIS; unique key namespace {cacheNamespace}.");
        output.WriteLine("Production wiring: AddModuleCache with FusionCache, System.Text.Json serializer, Redis distributed cache, and packaged Redis backplane.");
        output.WriteLine($"Measurement: durable PostgreSQL commit to first correct normal HybridCache lookup on warm host B; {RunCount} samples per case; {PollInterval.TotalMilliseconds:0}ms polling; fixed regression deadline={RegressionDeadline.TotalMilliseconds:0}ms; {SafetyTimeout.TotalSeconds:0}s finite experiment safety timeout (not an SLA).");

        foreach (var invalidation in Enum.GetValues<InvalidationKind>())
        foreach (var load in Enum.GetValues<ReadLoad>())
        {
            var samples = new List<TimeSpan>(RunCount);
            var failures = new List<string>();
            var staleObservations = 0;

            for (var run = 0; run < RunCount; run++)
            {
                var id = $"{invalidation}-{load}-{run}";
                var key = $"{cacheNamespace}lookup:{id}";
                var tag = $"{cacheNamespace}tag:{id}";
                await database.InsertAsync(id, 0);

                Assert.Equal(0, await hostA.ReadVersionAsync(database.TableName, id, key, tag));
                Assert.Equal(0, await hostB.ReadVersionAsync(database.TableName, id, key, tag));

                var committedAt = await database.UpdateAndCommitAsync(id, 1);
                try
                {
                    await hostA.InvalidateAsync(invalidation, key, tag);
                    var converged = false;
                    while (Stopwatch.GetElapsedTime(committedAt) < SafetyTimeout && !converged)
                    {
                        int[] observed;
                        if (load == ReadLoad.Concurrent)
                        {
                            observed = await Task.WhenAll(Enumerable.Range(0, 8)
                                .Select(_ => hostB.ReadVersionAsync(database.TableName, id, key, tag)));
                        }
                        else
                        {
                            observed = [await hostB.ReadVersionAsync(database.TableName, id, key, tag)];
                        }

                        staleObservations += observed.Count(value => value != 1);
                        if (observed.Any(value => value == 1))
                        {
                            converged = true;
                        }
                        else
                        {
                            await Task.Delay(PollInterval);
                        }
                    }

                    var elapsed = Stopwatch.GetElapsedTime(committedAt);
                    samples.Add(elapsed);
                    if (!converged)
                    {
                        failures.Add($"{id}: timed out at {elapsed.TotalMilliseconds:F1}ms");
                    }
                    else if (elapsed > RegressionDeadline)
                    {
                        failures.Add($"{id}: exceeded fixed {RegressionDeadline.TotalMilliseconds:0}ms regression deadline at {elapsed.TotalMilliseconds:F1}ms");
                    }
                }
                catch (Exception exception)
                {
                    var elapsed = Stopwatch.GetElapsedTime(committedAt);
                    samples.Add(elapsed);
                    failures.Add($"{id}: failed at {elapsed.TotalMilliseconds:F1}ms: {exception.GetType().Name}: {exception.Message}");
                }
            }

            WriteMeasurement(invalidation, load, samples, staleObservations, failures);
            Assert.Empty(failures);
        }
    }

    [RedisAndPostgresFact]
    public async Task Production_module_cache_records_late_factory_and_stampede_behavior()
    {
        var redis = RequiredEnvironmentVariable("VELOTIME_TEST_REDIS");
        var postgres = RequiredEnvironmentVariable("VELOTIME_TEST_POSTGRES");
        var cacheNamespace = $"velotimer:phase-one:late:{Guid.NewGuid():N}:";

        await using var database = await CacheDatabase.CreateAsync(postgres);
        await using var hostA = await CacheHost.CreateAsync(redis, postgres);
        await using var hostB = await CacheHost.CreateAsync(redis, postgres);

        foreach (var invalidation in Enum.GetValues<InvalidationKind>())
        {
            var id = $"late-{invalidation}";
            var key = $"{cacheNamespace}lookup:{id}";
            var tag = $"{cacheNamespace}tag:{id}";
            await database.InsertAsync(id, 0);
            var factoryRead = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var lateFactory = hostB.ReadWithPausedFactoryAsync(
                database.TableName, id, key, tag, factoryRead, releaseFactory.Task);
            var oldDatabaseValue = await factoryRead.Task.WaitAsync(SafetyTimeout);
            await database.UpdateAndCommitAsync(id, 1);
            await hostA.InvalidateAsync(invalidation, key, tag);
            releaseFactory.SetResult();
            var lateResult = await lateFactory.WaitAsync(SafetyTimeout);
            var normalLookupAfterLateCompletion = await hostB.ReadVersionAsync(database.TableName, id, key, tag);

            output.WriteLine(
                $"Late factory {invalidation}: factory read {oldDatabaseValue}, returned {lateResult} after commit+eviction; subsequent normal host-B lookup returned {normalLookupAfterLateCompletion}.");
            Assert.InRange(lateResult, 0, 1);
            Assert.InRange(normalLookupAfterLateCompletion, 0, 1);
        }

        var stampedeKey = $"{cacheNamespace}stampede";
        var factoryCalls = 0;
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            hostA.GetOrCreateAsync(stampedeKey, async cancellationToken =>
            {
                Interlocked.Increment(ref factoryCalls);
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                return 42;
            })));

        output.WriteLine($"Same-host stampede: 32 concurrent callers, factory executions={factoryCalls}, distinct results={results.Distinct().Count()}.");
        Assert.All(results, result => Assert.Equal(42, result));
        Assert.Equal(1, factoryCalls);
    }

    [RedisAndPostgresFact]
    public async Task Timing_module_tag_first_invalidation_converges_after_a_paused_old_factory()
    {
        var redis = RequiredEnvironmentVariable("VELOTIME_TEST_REDIS");
        var postgres = RequiredEnvironmentVariable("VELOTIME_TEST_POSTGRES");
        var namespaceId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var transponderId = Guid.NewGuid();
        var id = $"timing-late-{namespaceId:N}";
        var key = TimingCacheKeys.LastPassing(installationId, transponderId);
        var tag = TimingCacheTags.LastPassing(installationId, transponderId);

        await using var database = await CacheDatabase.CreateAsync(postgres);
        await using var hostA = await CacheHost.CreateAsync(redis, postgres);
        await using var hostB = await CacheHost.CreateAsync(redis, postgres);
        await database.InsertAsync(id, 0);

        var factoryRead = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateFactory = hostB.ReadWithPausedFactoryAsync(
            database.TableName, id, key, tag, factoryRead, releaseFactory.Task);
        Assert.Equal(0, await factoryRead.Task.WaitAsync(SafetyTimeout));

        var committedAt = await database.UpdateAndCommitAsync(id, 1);
        await hostA.InvalidateTimingAsync(key, tag);
        releaseFactory.SetResult();
        Assert.Equal(0, await lateFactory.WaitAsync(SafetyTimeout));

        var converged = false;
        while (Stopwatch.GetElapsedTime(committedAt) < RegressionDeadline && !converged)
        {
            converged = await hostB.ReadVersionAsync(database.TableName, id, key, tag) == 1;
            if (!converged)
            {
                await Task.Delay(PollInterval);
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(committedAt);
        output.WriteLine($"Timing production tag/key invalidation with paused old factory: deadline={RegressionDeadline.TotalMilliseconds:0}ms, elapsed={elapsed.TotalMilliseconds:F2}ms, current read observed={converged}.");
        Assert.True(converged, $"Warm host remained stale after the fixed {RegressionDeadline.TotalMilliseconds:0}ms regression deadline (observed {elapsed.TotalMilliseconds:F2}ms).");
    }

    [RedisAndPostgresFact]
    public async Task Production_module_cache_missed_backplane_notification_has_finite_stale_window_and_restart_recovers()
    {
        // Measurement only: missed backplane notifications are outside the phase-one
        // freshness guarantee. Production AddModuleCache defaults are not changed; only
        // host B's Redis pub/sub client is killed (CLIENT KILL ID) on the shared isolated
        // Redis, which stays up. Stale reads are recorded, never discarded.
        var redis = RequiredEnvironmentVariable("VELOTIME_TEST_REDIS");
        var postgres = RequiredEnvironmentVariable("VELOTIME_TEST_POSTGRES");
        var missedSafetyTimeout = TimeSpan.FromSeconds(180);
        var poll = TimeSpan.FromMilliseconds(5);
        var ns = Guid.NewGuid().ToString("N");
        var keyId = $"missed-key-{ns}";
        var tagId = $"missed-tag-{ns}";
        var exactKey = $"velotimer:phase-one:missed:{ns}:key";
        var taggedKey = $"velotimer:phase-one:missed:{ns}:tagged";
        var tag = $"velotimer:phase-one:missed:{ns}:tag";
        var unusedTag = $"velotimer:phase-one:missed:{ns}:unused";

        await using var admin = await ConnectionMultiplexer.ConnectAsync($"{redis},allowAdmin=true");
        var server = admin.GetServer(admin.GetEndPoints().Single());
        await using var database = await CacheDatabase.CreateAsync(postgres);
        await database.InsertAsync(keyId, 0);
        await database.InsertAsync(tagId, 0);

        await using var hostA = await CacheHost.CreateAsync(redis, postgres);
        Assert.Equal(0, await hostA.ReadVersionAsync(database.TableName, keyId, exactKey, unusedTag));
        var beforeB = await PubSubClientIdsAsync(server, TimeSpan.Zero, []);
        await using var hostB = await CacheHost.CreateAsync(redis, postgres);
        Assert.Equal(0, await hostB.ReadVersionAsync(database.TableName, keyId, exactKey, unusedTag));
        Assert.Equal(0, await hostB.ReadVersionAsync(database.TableName, tagId, taggedKey, tag));
        var warmedAt = Stopwatch.GetTimestamp();
        var hostBSubscribers = (await PubSubClientIdsAsync(server, SafetyTimeout, beforeB)).Except(beforeB).ToArray();
        Assert.NotEmpty(hostBSubscribers);

        await database.UpdateAndCommitAsync(keyId, 1);
        var committedAt = await database.UpdateAndCommitAsync(tagId, 1);
        // Keep killing host B pub/sub clients (including automatic reconnects) for the
        // whole invalidation publish plus 200ms, so the notification is deterministically missed.
        var killed = new HashSet<long>();
        using var stopKilling = new CancellationTokenSource();
        var killer = Task.Run(async () =>
        {
            while (!stopKilling.IsCancellationRequested)
            {
                foreach (var id in (await PubSubClientIdsAsync(server, TimeSpan.Zero, [])).Except(beforeB))
                {
                    await server.ClientKillAsync(id, ClientType.PubSub);
                    lock (killed) killed.Add(id);
                }
                await Task.Delay(1);
            }
        });
        await Task.Delay(50);
        await hostA.InvalidateAsync(InvalidationKind.ExactKey, exactKey, unusedTag);
        await hostA.InvalidateAsync(InvalidationKind.Tag, taggedKey, tag);
        await Task.Delay(200);
        var keyDuringOutage = await hostB.ReadVersionAsync(database.TableName, keyId, exactKey, unusedTag);
        var tagDuringOutage = await hostB.ReadVersionAsync(database.TableName, tagId, taggedKey, tag);
        stopKilling.Cancel();
        await killer;
        var killingStoppedAt = Stopwatch.GetTimestamp();
        var resubscribedIds = (await PubSubClientIdsAsync(server, SafetyTimeout, beforeB.Concat(killed))).Except(beforeB).Except(killed).ToArray();
        TimeSpan? resubscribed = resubscribedIds.Length > 0 ? Stopwatch.GetElapsedTime(killingStoppedAt) : null;

        TimeSpan? keyFresh = null, tagFresh = null;
        int keyStale = 0, tagStale = 0;
        while ((keyFresh is null || tagFresh is null) && Stopwatch.GetElapsedTime(committedAt) < missedSafetyTimeout)
        {
            if (keyFresh is null)
            {
                if (await hostB.ReadVersionAsync(database.TableName, keyId, exactKey, unusedTag) == 1) keyFresh = Stopwatch.GetElapsedTime(committedAt);
                else keyStale++;
            }
            if (tagFresh is null)
            {
                if (await hostB.ReadVersionAsync(database.TableName, tagId, taggedKey, tag) == 1) tagFresh = Stopwatch.GetElapsedTime(committedAt);
                else tagStale++;
            }
            await Task.Delay(poll);
        }

        await using (var restartedB = await CacheHost.CreateAsync(redis, postgres))
        {
            Assert.Equal(1, await restartedB.ReadVersionAsync(database.TableName, keyId, exactKey, unusedTag));
            Assert.Equal(1, await restartedB.ReadVersionAsync(database.TableName, tagId, taggedKey, tag));
        }

        var missed = keyDuringOutage == 0 || tagDuringOutage == 0;
        output.WriteLine($"Missed-notification measurement: environment={RuntimeInformation.FrameworkDescription}; production defaults (Duration=5min, MemoryCacheDuration=1min); initial host B pub/sub client ids=[{string.Join(",", hostBSubscribers)}], kills during window={killed.Count}; poll={poll.TotalMilliseconds:0}ms; safety timeout={missedSafetyTimeout.TotalSeconds:0}s (not an SLA).");
        output.WriteLine($"Notification missed by host B={missed} (read during outage: key={keyDuringOutage}, tag={tagDuringOutage}; 1=fresh); host B resubscribed after kill window={(resubscribed?.TotalMilliseconds.ToString("F0") ?? "not observed")}ms; warm-to-commit={Stopwatch.GetElapsedTime(warmedAt, committedAt).TotalMilliseconds:F0}ms.");
        output.WriteLine($"After kill window ended: exact key fresh {(keyFresh?.TotalMilliseconds.ToString("F0") ?? "TIMEOUT")}ms after commit, stale reads={keyStale}; tag fresh {(tagFresh?.TotalMilliseconds.ToString("F0") ?? "TIMEOUT")}ms after commit, stale reads={tagStale}. A restarted host B (new process-local cache) read fresh values on first lookup.");
        Assert.NotNull(keyFresh);
        Assert.NotNull(tagFresh);
    }

    private static async Task<HashSet<long>> PubSubClientIdsAsync(IServer server, TimeSpan waitForNew, IEnumerable<long> known)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            var ids = (await server.ClientListAsync()).Where(client => client.ClientType == ClientType.PubSub).Select(client => client.Id).ToHashSet();
            if (waitForNew == TimeSpan.Zero || ids.Except(known).Any() || Stopwatch.GetElapsedTime(started) > waitForNew)
            {
                return ids;
            }
            await Task.Delay(PollInterval);
        }
    }

    private void WriteMeasurement(
        InvalidationKind invalidation,
        ReadLoad load,
        IReadOnlyCollection<TimeSpan> samples,
        int staleObservations,
        IReadOnlyCollection<string> failures)
    {
        var sorted = samples.OrderBy(sample => sample).ToArray();
        var median = sorted.Length == 0
            ? TimeSpan.Zero
            : sorted.Length % 2 == 0
                ? TimeSpan.FromTicks((sorted[sorted.Length / 2 - 1].Ticks + sorted[sorted.Length / 2].Ticks) / 2)
                : sorted[sorted.Length / 2];
        var p95Index = sorted.Length == 0 ? 0 : Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1);
        output.WriteLine(
            $"Propagation {invalidation}/{load}: runs={samples.Count}, median={median.TotalMilliseconds:F2}ms, p95-observation={sorted.ElementAtOrDefault(p95Index).TotalMilliseconds:F2}ms, max={sorted.LastOrDefault().TotalMilliseconds:F2}ms, stale reads before convergence={staleObservations}, fixed-deadline failures={failures.Count}.");
        foreach (var failure in failures)
        {
            output.WriteLine($"FAILED SAMPLE: {failure}");
        }
    }

    private static string RequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} must be configured for this real Redis/PostgreSQL integration test.");

    private enum InvalidationKind
    {
        ExactKey,
        Tag
    }

    private enum ReadLoad
    {
        Idle,
        Concurrent
    }

    private sealed class CacheHost : IAsyncDisposable
    {
        private readonly IHost host;

        private CacheHost(IHost host)
        {
            this.host = host;
        }

        public static async Task<CacheHost> CreateAsync(string redis, string postgres)
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:cache"] = redis
            });
            builder.AddModuleCache();
            builder.Services.AddScoped(_ => new NpgsqlConnection(postgres));

            var host = builder.Build();
            await host.StartAsync();
            return new CacheHost(host);
        }

        public async Task<int> ReadVersionAsync(string table, string id, string key, string tag)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();
            var connection = scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();
            return await cache.GetOrCreateAsync<int>(
                key,
                cancellationToken => ReadVersionFromDatabaseAsync(connection, table, id, cancellationToken),
                tags: [tag],
                cancellationToken: CancellationToken.None);
        }

        public async Task<int> ReadWithPausedFactoryAsync(
            string table,
            string id,
            string key,
            string tag,
            TaskCompletionSource<int> factoryRead,
            Task releaseFactory)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();
            var connection = scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();
            return await cache.GetOrCreateAsync<int>(
                key,
                async cancellationToken =>
                {
                    var value = await ReadVersionFromDatabaseAsync(connection, table, id, cancellationToken);
                    factoryRead.TrySetResult(value);
                    await releaseFactory.WaitAsync(cancellationToken);
                    return value;
                },
                tags: [tag],
                cancellationToken: CancellationToken.None);
        }

        public async Task<int> GetOrCreateAsync(string key, Func<CancellationToken, Task<int>> factory)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();
            return await cache.GetOrCreateAsync<int>(
                key,
                async cancellationToken => await factory(cancellationToken),
                cancellationToken: CancellationToken.None);
        }

        public async Task InvalidateAsync(InvalidationKind kind, string key, string tag)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();
            if (kind == InvalidationKind.ExactKey)
            {
                await cache.RemoveAsync(key, CancellationToken.None);
            }
            else
            {
                await cache.RemoveByTagAsync(tag, CancellationToken.None);
            }
        }

        public async Task InvalidateTimingAsync(string key, string tag)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();
            await TimingCacheInvalidation.InvalidateAsync(cache, NullLogger.Instance, [key], [tag]);
        }

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class CacheDatabase : IAsyncDisposable
    {
        private readonly string connectionString;

        private CacheDatabase(string connectionString, string tableName, string serverVersion)
        {
            this.connectionString = connectionString;
            TableName = tableName;
            ServerVersion = serverVersion;
        }

        public string TableName { get; }
        public string ServerVersion { get; }

        public static async Task<CacheDatabase> CreateAsync(string connectionString)
        {
            var tableName = $"vtcache_{Guid.NewGuid():N}";
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"CREATE TABLE public.\"{tableName}\" (id text PRIMARY KEY, version integer NOT NULL)";
                await command.ExecuteNonQueryAsync();
            }

            return new CacheDatabase(connectionString, tableName, connection.PostgreSqlVersion.ToString());
        }

        public async Task InsertAsync(string id, int version)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO public.\"{TableName}\" (id, version) VALUES (@id, @version)";
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("version", version);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> UpdateAndCommitAsync(string id, int version)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"UPDATE public.\"{TableName}\" SET version = @version WHERE id = @id";
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("version", version);
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }

            await transaction.CommitAsync();
            return Stopwatch.GetTimestamp();
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP TABLE IF EXISTS public.\"{TableName}\"";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async ValueTask<int> ReadVersionFromDatabaseAsync(
        NpgsqlConnection connection,
        string table,
        string id,
        CancellationToken cancellationToken)
    {
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT version FROM public.\"{table}\" WHERE id = @id";
            command.Parameters.AddWithValue("id", id);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is int version
                ? version
                : throw new InvalidOperationException($"No row exists for the isolated cache fixture key '{id}'.");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private sealed class RedisAndPostgresFactAttribute : FactAttribute
    {
        public RedisAndPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS"))
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_POSTGRES")))
            {
                Skip = "Requires VELOTIME_TEST_REDIS and VELOTIME_TEST_POSTGRES; no in-memory substitute is used.";
            }
        }
    }
}
