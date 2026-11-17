using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class StatisticsConfigurationLockPostgreSqlTests
{
    // Deliberately ordered PostgreSQL UUIDs: the moved owner sorts BEFORE every old lock.
    private static readonly Guid Low = Guid.Parse("01000000-0000-0000-0000-000000000000");
    private static readonly Guid A = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid B = Guid.Parse("20000000-0000-0000-0000-000000000000");
    private static readonly Guid Multi = Guid.Parse("d0000000-0000-0000-0000-000000000000");

    [PublicStatisticsPostgreSqlFact]
    public async Task ApiCreateAndRealCompletionCommitWithoutDeadlockWithOverlappingOwnersAndDependents()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var gate = new AfterFirstLock();
        await using var host = await PublicStatisticsTestHost.CreateAsync(true, services =>
            services.ConfigureDbContext<StatisticsDbContext>((_, options) => options.AddInterceptors(gate)));
        var parentA = Parent(A);
        var parentB = Parent(B);
        parentB.CoursePointStart = PublicStatisticsTestHost.Start;
        parentB.CoursePointEnd = PublicStatisticsTestHost.End;
        var rider = Guid.NewGuid();
        var start = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
        await host.SeedAsync(Items().Concat(new object[]
        {
            parentA, parentB, Child(Multi, parentA.Id), Child(Multi, parentB.Id),
            new Sample
            {
                TransponderId = rider, FacilityId = PublicStatisticsTestHost.Facility,
                CoursePointStartId = parentB.CoursePointStart, CoursePointEndId = parentB.CoursePointEnd,
                TimeStart = start, TimeEnd = start.AddSeconds(10), Duration = TimeSpan.FromSeconds(10),
                Distance = 250, Speed = 25
            }
        }).ToArray());
        await using var worker = await NewContext(host);
        await new StatisticsValidityScheduler(worker).EnsureJobsForAllItemsAsync(ct);
        await worker.Database.OpenConnectionAsync(ct);
        var workerPid = ((NpgsqlConnection)worker.Database.GetDbConnection()).ProcessID;
        using var client = host.App.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");

        // Pause AFTER the first API exclusive lock statement. Old code holds A,Multi;
        // completion holds B and waits on Multi, then the API asks for B (40P01).
        var apiTask = client.PostAsJsonAsync("/api/statistics/admin/configurations",
            new SaveStatisticsConfigurationRequest(B, StatisticsConfigurationType.Multi, null, null, parentA.Id, 1), ct);
        Task<StatisticsEntry[]>? completionTask = null;
        try
        {
            await gate.Locked.Task.WaitAsync(ct);
            completionTask = Complete();
            await WaitForLock(worker, workerPid, ct);
            gate.Release.TrySetResult();
            using var response = await apiTask.WaitAsync(ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var saved = (await response.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>(ct))!;
            Assert.Equal(B, saved.StatisticsItemId);
            Assert.Equal(parentA.Id, saved.ParentConfigId);
            Assert.Equal(StatisticsConfigurationType.Multi, saved.Type);
            var completed = Assert.Single(await completionTask.WaitAsync(ct));
            Assert.Equal(parentB.Id, completed.StatisticsItemConfigId);
            Assert.Equal(new[] { A, B, Multi }, gate.FirstScope.Order().ToArray());
            Assert.Empty(gate.Deadlocks); // Endpoint retries must not hide a regression.

            await using var verify = await NewContext(host);
            Assert.Equal(3, await verify.Set<MultiStatisticsItemConfig>().CountAsync(ct));
            Assert.True(await verify.Set<MultiStatisticsItemConfig>().AnyAsync(c => c.Id == saved.Id, ct));
            var persisted = Assert.Single(await verify.Set<StatisticsEntry>().ToListAsync(ct));
            Assert.Equal(completed.Id, persisted.Id);
            Assert.True(persisted.IsValid);
            Assert.Equal(start, persisted.TimeStart);
            Assert.Equal(DateTimeKind.Utc, persisted.TimeStart.Kind);
            Assert.Equal(start.AddSeconds(10), persisted.TimeEnd);
            Assert.Equal(TimeSpan.FromSeconds(10), persisted.Duration);
            Assert.Equal(25d, persisted.Speed); // Persisted m/s, not km/h.
        }
        finally
        {
            gate.Release.TrySetResult();
            if (!apiTask.IsCompleted || completionTask is { IsCompleted: false }) timeout.Cancel();
            // Observe both operations before database disposal, preserving the primary failure.
            try { await apiTask; } catch { }
            if (completionTask is not null) { try { await completionTask; } catch { } }
        }

        async Task<StatisticsEntry[]> Complete()
        {
            await using var transaction = await worker.Database.BeginTransactionAsync(ct);
            var service = new StatisticsService(worker, host.App.Services.GetRequiredService<HybridCache>());
            var entries = (await service.ProcessCompletedAsync(rider, start, start.AddSeconds(10),
                parentB.CoursePointStart, parentB.CoursePointEnd, ct)).ToArray();
            await transaction.CommitAsync(ct);
            return entries;
        }
    }

    [PublicStatisticsPostgreSqlFact]
    public Task DependentMoveReleasesOldScopeBeforeRediscovery() => MovingScope();

    [PublicStatisticsPostgreSqlFact]
    public Task NewDependentIsIncludedBeforeReturning() => MovingScope(addDependent: true);

    [PublicStatisticsPostgreSqlFact]
    public Task ParentMoveReleasesWholeConfigurationScopeBeforeRediscovery() => MovingScope(moveParent: true);

    [PublicStatisticsPostgreSqlFact]
    public Task DependencyRediscoveryContinuesBeyondAFewRetries() => MovingScope(changes: 6);

    [PublicStatisticsPostgreSqlFact]
    public Task ParentRediscoveryContinuesBeyondAFewRetries() => MovingScope(moveParent: true, changes: 6);

    [PublicStatisticsPostgreSqlFact]
    public Task SubsequentSchedulingDoesNotExtendValidatedScope() => MovingScope(schedule: true);

    private static async Task MovingScope(bool addDependent = false, bool moveParent = false,
        int changes = 1, bool schedule = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var parent = Parent(A);
        var child = Child(Multi, parent.Id);
        await host.SeedAsync(Items().Concat(addDependent ? new object[] { parent } : [parent, child]).ToArray());
        await using (var seed = await NewContext(host))
            await new StatisticsValidityScheduler(seed).EnsureJobsForAllItemsAsync(ct);
        var marker = Guid.NewGuid().ToString();
        var gate = new BeforeEachLock();
        gate.BeforeLock = async attempt =>
        {
            if (attempt > changes) return;
            await using var mutate = await NewContext(host);
            await using var mutation = await mutate.Database.BeginTransactionAsync(ct);
            await mutate.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '1000ms'", ct);
            // From attempt 2 onward this succeeds ONLY if rollback released old scope locks.
            await new StatisticsValidityScheduler(mutate).LockItemsAsync([Low, A, B, Multi],
                StatisticsItemLockMode.Exclusive, ct);
            await using (var command = mutate.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "SELECT pg_try_advisory_xact_lock(hashtextextended(@key, 0))";
                command.Parameters.Add(new NpgsqlParameter("key", marker));
                Assert.Equal(false, await command.ExecuteScalarAsync(ct)); // Outer transaction survived.
            }
            if (moveParent)
                (await mutate.Set<SimpleStatisticsItemConfig>().SingleAsync(ct)).StatisticsItemId = attempt % 2 == 1 ? Low : A;
            else if (addDependent)
                mutate.Add(Child(Low, parent.Id));
            else
                (await mutate.Set<MultiStatisticsItemConfig>().SingleAsync(ct)).StatisticsItemId = attempt % 2 == 1 ? Low : Multi;
            await mutate.SaveChangesAsync(ct);
            await mutation.CommitAsync(ct);
        };
        await using var api = await NewContext(host, gate);
        await using var transaction = await api.Database.BeginTransactionAsync(ct);
        await api.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({marker}, 0))", ct);
        var scheduler = new StatisticsValidityScheduler(api);
        // Exercise the exact API helper inside a caller-owned transaction so savepoint
        // rollback and preservation of pre-existing transaction work are observable.
        var method = typeof(Program).Assembly
            .GetType("VeloTime.Module.Statistics.Api.Endpoints.StatisticsDefinitionEndpoints")!
            .GetMethod("LockConfigurationScope", BindingFlags.NonPublic | BindingFlags.Static)!;
        var request = new SaveStatisticsConfigurationRequest(B, StatisticsConfigurationType.Multi, null, null, parent.Id, 1);
        await ((Task)method.Invoke(null, new object?[] { request, null, api, scheduler, ct })!).WaitAsync(ct);
        Assert.Equal(changes + 1, gate.Scopes.Count);
        var finalScope = gate.Scopes.Last().Order().ToArray();
        var currentOwner = changes % 2 == 1 ? Low : moveParent ? A : Multi;
        var expected = moveParent ? new[] { currentOwner, B, Multi } : new[] { A, B, currentOwner };
        Assert.Equal(expected.Order(), finalScope);
        Assert.All(gate.Scopes, scope => Assert.Equal(scope.Length, scope.Distinct().Count()));

        // Releasing nested savepoints must NOT release the successful scope locks.
        await using (var contender = await NewContext(host))
        {
            await using var competing = await contender.Database.BeginTransactionAsync(ct);
            await contender.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '200ms'", ct);
            var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new StatisticsValidityScheduler(contender).LockItemsAsync([currentOwner], StatisticsItemLockMode.Exclusive, ct));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, Assert.IsType<PostgresException>(blocked.InnerException).SqlState);
        }
        if (schedule)
        {
            var attempts = gate.Scopes.Count;
            await scheduler.ScheduleThresholdChangeAsync(A, true, ct);
            Assert.All(gate.Scopes.Skip(attempts), scope => Assert.All(scope, id => Assert.Contains(id, finalScope)));
        }
        // A real write commits through the original transaction after all rediscoveries.
        api.Add(Child(B, parent.Id));
        await api.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await using var verify = await NewContext(host);
        Assert.Equal(2, await verify.Set<MultiStatisticsItemConfig>().CountAsync(ct));
        Assert.Equal(moveParent ? currentOwner : A,
            (await verify.Set<SimpleStatisticsItemConfig>().SingleAsync(ct)).StatisticsItemId);
        if (!moveParent)
            Assert.True(await verify.Set<MultiStatisticsItemConfig>().AnyAsync(c => c.StatisticsItemId == currentOwner, ct));
        if (schedule)
            Assert.All(await verify.Set<StatisticsValidityRevalidation>().Where(j => j.StatisticsItemId == A || j.StatisticsItemId == Low)
                .ToListAsync(ct), job => { Assert.True(job.IsPending); Assert.Equal(2, job.Generation); });
        // Commit must release all final locks too.
        await using var afterCommit = await verify.Database.BeginTransactionAsync(ct);
        await verify.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '1000ms'", ct);
        await new StatisticsValidityScheduler(verify).LockItemsAsync(finalScope, StatisticsItemLockMode.Exclusive, ct);
    }

    private static object[] Items() => new[] { Low, A, B, Multi }
        .Select(id => (object)new StatisticsItem { Id = id, Name = id.ToString(), Distance = 250 }).ToArray();

    private static SimpleStatisticsItemConfig Parent(Guid item) => new()
    {
        Id = Guid.NewGuid(), StatisticsItem = null!, StatisticsItemId = item,
        CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid()
    };

    private static MultiStatisticsItemConfig Child(Guid item, Guid parent) => new()
    {
        Id = Guid.NewGuid(), StatisticsItem = null!, ParentConfig = null!,
        StatisticsItemId = item, ParentConfigId = parent, Repetitions = 1
    };

    private static async Task<StatisticsDbContext> NewContext(PublicStatisticsTestHost host, IInterceptor? interceptor = null)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>().Database.GetConnectionString();
        var options = new DbContextOptionsBuilder<StatisticsDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention();
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new StatisticsDbContext(options.Options);
    }

    private static async Task WaitForLock(StatisticsDbContext worker, int pid, CancellationToken ct)
    {
        await using var observer = new NpgsqlConnection(worker.Database.GetConnectionString());
        await observer.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = @pid AND wait_event_type = 'Lock')", observer);
        command.Parameters.AddWithValue("pid", pid);
        while (await command.ExecuteScalarAsync(ct) is not true) await Task.Delay(10, ct);
    }

    private sealed class AfterFirstLock : DbCommandInterceptor
    {
        public Guid[] FirstScope { get; private set; } = [];
        public TaskCompletionSource Locked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<PostgresException> Deadlocks { get; } = [];

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader reader, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FOR UPDATE") && !Locked.Task.IsCompleted)
            {
                FirstScope = (Guid[])command.Parameters["ids"].Value!;
                Locked.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return reader;
        }

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData data, CancellationToken ct = default)
        {
            if (data.Exception is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected } error)
                Deadlocks.Add(error);
            return Task.CompletedTask;
        }
    }

    private sealed class BeforeEachLock : DbCommandInterceptor
    {
        public Func<int, Task> BeforeLock { get; set; } = _ => Task.CompletedTask;
        public List<Guid[]> Scopes { get; } = [];
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FOR UPDATE"))
            {
                Assert.Contains("ORDER BY", command.CommandText);
                Scopes.Add((Guid[])command.Parameters["ids"].Value!);
                await BeforeLock(Scopes.Count);
            }
            return result;
        }
    }
}
