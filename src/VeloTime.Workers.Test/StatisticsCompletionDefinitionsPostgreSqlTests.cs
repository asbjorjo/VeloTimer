using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Workers.Test;

public sealed class PostgreSqlStatisticsTheoryAttribute : TheoryAttribute
{
    public PostgreSqlStatisticsTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_STATISTICS_POSTGRES")))
            Skip = "Set VELOTIME_TEST_STATISTICS_POSTGRES to run PostgreSQL statistics tests.";
    }
}

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task CompletionIgnoresCachedAndTrackedEmptyRequirementsAndReplaysPersistedFailure()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var config = Simple();
        var rider = Guid.NewGuid();
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        await using var processor = Db();
        processor.AddRange(config, CompletionSample(config, rider));
        await processor.SaveChangesAsync(token);
        var service = new StatisticsService(processor, services.GetRequiredService<HybridCache>());
        var cached = Assert.Single(await service.GetStatisticsItemConfigByEndCoursePoint(config.CoursePointEnd, token));
        await using (var adminDb = Db())
        {
            await using var tx = await adminDb.Database.BeginTransactionAsync(token);
            await new StatisticsValidityScheduler(adminDb).LockItemsAsync([config.StatisticsItemId], StatisticsItemLockMode.Exclusive, token);
            var current = await adminDb.Set<SimpleStatisticsItemConfig>().SingleAsync(token);
            current.RequiredCoursePointIds = [Guid.NewGuid()];
            current.RequiredCourseLayoutId = Guid.NewGuid();
            await adminDb.SaveChangesAsync(token);
            await tx.CommitAsync(token);
        }
        Assert.Empty(cached.RequiredCoursePointIds);
        Assert.Empty(config.RequiredCoursePointIds); // Deliberately keep EF tracking stale too.
        Guid? entryId = null;
        for (var delivery = 0; delivery < 2; delivery++)
        {
            await using var tx = await processor.Database.BeginTransactionAsync(token);
            var row = Assert.Single(await Complete(service, config, rider, config.CoursePointEnd, token));
            Assert.False(row.RequiredCoursePointsSatisfied);
            Assert.Equal(StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied, row.InvalidReason);
            entryId ??= row.Id;
            Assert.Equal(entryId.Value, row.Id);
            await tx.CommitAsync(token);
        }
        await using var verify = Db();
        Assert.False((await verify.Set<StatisticsEntry>().SingleAsync(token)).RequiredCoursePointsSatisfied);
    }

    [PostgreSqlStatisticsFact]
    public async Task CancelledCompletionLockWaitFailsAndCanBeRedeliveredInSameContext()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var token = timeout.Token;
        var config = Simple();
        var rider = Guid.NewGuid();
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        await using var adminDb = Db();
        adminDb.AddRange(config, CompletionSample(config, rider));
        await adminDb.SaveChangesAsync(token);
        await using var adminTx = await adminDb.Database.BeginTransactionAsync(token);
        await new StatisticsValidityScheduler(adminDb).LockItemsAsync([config.StatisticsItemId], StatisticsItemLockMode.Exclusive, token);
        config.RequiredCoursePointIds = [Guid.NewGuid()];
        await adminDb.SaveChangesAsync(token);
        await using var processor = Db();
        var service = new StatisticsService(processor, services.GetRequiredService<HybridCache>());
        await processor.Database.OpenConnectionAsync(token);
        var ownership = new StatisticsOwnershipService(processor, new RequiredPointEmptyOwnershipReader());
        var completion = ownership.CreateEntriesAsync(rider,
            ct => Complete(service, config, rider, config.CoursePointEnd, ct), cancelled.Token);
        await WaitForCompletionLockWait(processor, token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
        await adminTx.CommitAsync(token);
        Assert.Null(processor.Database.CurrentTransaction);
        Assert.Empty(await adminDb.Set<StatisticsEntry>().AsNoTracking().ToListAsync(token));
        var retried = Assert.Single(await ownership.CreateEntriesAsync(rider,
            ct => Complete(service, config, rider, config.CoursePointEnd, ct), token));
        Assert.False(retried.RequiredCoursePointsSatisfied);
        Assert.False((await adminDb.Set<StatisticsEntry>().AsNoTracking().SingleAsync(token)).RequiredCoursePointsSatisfied);
    }

    [PostgreSqlStatisticsTheory]
    [InlineData("requirements")]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("endIn")]
    [InlineData("endOut")]
    [InlineData("start")]
    [InlineData("owner")]
    [InlineData("dependentAdd")]
    [InlineData("dependentDelete")]
    [InlineData("dependentMove")]
    [InlineData("dependentReparent")]
    public async Task CompletionReloadsDefinitionsAfterConcurrentAdminAndRetriesChangedScope(string change)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var config = Simple();
        var rider = Guid.NewGuid();
        var endPoint = config.CoursePointEnd;
        var target = new StatisticsItem { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "Moved", Distance = 500 };
        var childItem = new StatisticsItem { Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), Name = "Multi", Distance = 500 };
        var unrelated = Simple();
        var child = new MultiStatisticsItemConfig
        {
            Id = Guid.NewGuid(), StatisticsItem = childItem, StatisticsItemId = childItem.Id,
            ParentConfig = config, ParentConfigId = config.Id, Repetitions = 1
        };
        if (change == "endIn") config.CoursePointEnd = Guid.NewGuid();
        var sample = CompletionSample(config, rider, endPoint);
        await using (var seed = Db())
        {
            seed.AddRange(target, childItem, unrelated, sample);
            if (change != "add") seed.Add(config);
            if (change is "dependentDelete" or "dependentMove" or "dependentReparent") seed.Add(child);
            await seed.SaveChangesAsync(token);
            await new StatisticsValidityScheduler(seed).EnsureJobsForAllItemsAsync(token);
        }
        var gate = new CompletionDefinitionGate { HoldRetry = change == "owner" };
        await using var processor = new StatisticsDbContext(new DbContextOptionsBuilder<StatisticsDbContext>()
            .UseNpgsql(connection).UseSnakeCaseNamingConvention().AddInterceptors(gate).Options);
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        // Warm the distributed/local definition cache too. Never invalidate it in this test.
        await new StatisticsService(processor, services.GetRequiredService<HybridCache>())
            .GetStatisticsItemConfigByEndCoursePoint(endPoint, token);
        gate.Enabled = true;
        await using var processorTx = await processor.Database.BeginTransactionAsync(token);
        var transponderKey = $"statistics-transponder:{rider:D}";
        await processor.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({transponderKey}, 0))", token);
        var service = new StatisticsService(processor, services.GetRequiredService<HybridCache>());
        var completion = Complete(service, config, rider, endPoint, token);
        try
        {
            await gate.Discovered.Task.WaitAsync(token);
            await using var adminDb = Db();
            await using var adminTx = await adminDb.Database.BeginTransactionAsync(token);
            var scheduler = new StatisticsValidityScheduler(adminDb);
            // Admin uses the existing item protocol, with both old/new owners and dependencies.
            await scheduler.LockItemsAsync([config.StatisticsItemId, target.Id, childItem.Id, unrelated.StatisticsItemId],
                StatisticsItemLockMode.Exclusive, token);
            var current = await adminDb.Set<SimpleStatisticsItemConfig>().SingleOrDefaultAsync(c => c.Id == config.Id, token);
            var dependent = await adminDb.Set<MultiStatisticsItemConfig>().SingleOrDefaultAsync(c => c.Id == child.Id, token);
            switch (change)
            {
                case "requirements": current!.RequiredCoursePointIds = [Guid.NewGuid()]; break;
                case "add":
                    adminDb.Add(new SimpleStatisticsItemConfig { Id = config.Id, StatisticsItem = null!,
                        StatisticsItemId = target.Id, CoursePointStart = config.CoursePointStart, CoursePointEnd = endPoint });
                    break;
                case "delete": adminDb.Remove(current!); break;
                case "endIn": current!.CoursePointEnd = endPoint; break;
                case "endOut": current!.CoursePointEnd = Guid.NewGuid(); break;
                case "start": current!.CoursePointStart = Guid.NewGuid(); break;
                case "owner": current!.StatisticsItemId = target.Id; break;
                case "dependentAdd":
                    adminDb.Add(new MultiStatisticsItemConfig { StatisticsItem = null!, StatisticsItemId = childItem.Id,
                        ParentConfig = null!, ParentConfigId = config.Id, Repetitions = 1 });
                    break;
                case "dependentDelete": adminDb.Remove(dependent!); break;
                case "dependentMove": dependent!.StatisticsItemId = target.Id; break;
                case "dependentReparent": dependent!.ParentConfigId = unrelated.Id; break;
            }
            await adminDb.SaveChangesAsync(token);
            if (change is "add" or "endIn") await adminTx.CommitAsync(token);
            gate.Release.TrySetResult();
            if (change is not ("add" or "endIn"))
            {
                // The worker has discovered OLD definitions and really waits on the admin lock.
                await WaitForCompletionLockWait(processor, token);
                Assert.False(completion.IsCompleted);
            }
            if (change is not ("add" or "endIn")) await adminTx.CommitAsync(token);
            if (change == "owner")
            {
                await gate.Retry.Task.WaitAsync(token);
                await using var observer = Db();
                await using var observerTx = await observer.Database.BeginTransactionAsync(token);
                await observer.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = 1000", token);
                // The old item lock must have been released, not retained and extended out of order.
                await new StatisticsValidityScheduler(observer).LockItemsAsync([config.StatisticsItemId], StatisticsItemLockMode.Exclusive, token);
                await using var command = observer.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT pg_try_advisory_xact_lock(hashtextextended(@key, 0))";
                command.Parameters.Add(new NpgsqlParameter("key", transponderKey));
                Assert.Equal(false, await command.ExecuteScalarAsync(token)); // Caller lock survives retry.
                Assert.Empty(await observer.Set<StatisticsEntry>().ToListAsync(token));
                await observerTx.CommitAsync(token);
                gate.ReleaseRetry.TrySetResult();
            }
            var rows = (await completion).ToList();
            if (change is "delete" or "endOut" or "start") Assert.Empty(rows);
            else
            {
                var row = Assert.Single(rows);
                Assert.Equal(config.Id, row.StatisticsItemConfigId);
                Assert.Equal(change is "owner" or "add" ? target.Id : config.StatisticsItemId, row.StatisticsItemId);
                Assert.Equal(change is "owner" or "add" ? 50 : 25, row.Speed);
                Assert.Equal(change != "requirements", row.RequiredCoursePointsSatisfied);
            }
            if (change is "owner" or "dependentAdd" or "dependentMove" or "dependentDelete" or "dependentReparent")
                Assert.True(gate.Scopes.Count >= 2);
            if (change is "dependentDelete" or "dependentReparent")
                Assert.False(gate.Scopes.Last().Exclusive);
            if (change is "dependentAdd" or "dependentMove")
            {
                var dependentOwner = change == "dependentMove" ? target.Id : childItem.Id;
                Assert.Contains(dependentOwner, gate.Scopes.Last().Ids);
                Assert.True(gate.Scopes.Last().Exclusive);
                Assert.Equal(2, (await processor.Set<StatisticsValidityRevalidation>().AsNoTracking()
                    .SingleAsync(j => j.StatisticsItemId == dependentOwner, token)).Generation);
            }
            await processorTx.CommitAsync(token);
            await using var verify = Db();
            Assert.Equal(rows.Count, await verify.Set<StatisticsEntry>().CountAsync(token));
        }
        finally
        {
            gate.Release.TrySetResult();
            gate.ReleaseRetry.TrySetResult();
            // Observe cancellation/failure; disposal rolls back the caller transaction, never acknowledges it.
            if (!completion.IsCompleted) timeout.Cancel();
            try { await completion; } catch when (token.IsCancellationRequested) { }
        }
    }

    private static Sample CompletionSample(SimpleStatisticsItemConfig config, Guid rider, Guid? endPoint = null) => new()
    {
        Id = Guid.NewGuid(), TransponderId = rider, FacilityId = Guid.NewGuid(), CoursePointStartId = config.CoursePointStart,
        CoursePointEndId = endPoint ?? config.CoursePointEnd, TimeStart = Start, TimeEnd = Start.AddSeconds(10),
        Duration = TimeSpan.FromSeconds(10), Distance = 250, Speed = 25
    };

    private static Task<IEnumerable<StatisticsEntry>> Complete(StatisticsService service, SimpleStatisticsItemConfig config,
        Guid rider, Guid endPoint, CancellationToken token) => service.ProcessCompletedAsync(rider, Start, Start.AddSeconds(10),
            config.CoursePointStart, endPoint, token);

    private async Task WaitForCompletionLockWait(StatisticsDbContext processor, CancellationToken token)
    {
        var pid = ((NpgsqlConnection)processor.Database.GetDbConnection()).ProcessID;
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync(token);
        while (true)
        {
            await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = @pid AND wait_event_type = @wait)", observer);
            command.Parameters.AddWithValue("pid", pid);
            command.Parameters.AddWithValue("wait", "Lock");
            if (await command.ExecuteScalarAsync(token) is true) return;
            await Task.Delay(10, token);
        }
    }

    private sealed class CompletionDefinitionGate : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public bool HoldRetry { get; init; }
        public TaskCompletionSource Retry { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRetry { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Discovered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(Guid[] Ids, bool Exclusive)> Scopes { get; } = [];

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("FOR "))
            {
                Scopes.Add(((Guid[])command.Parameters["ids"].Value!, command.CommandText.Contains("FOR UPDATE")));
                if (HoldRetry && Scopes.Count == 2)
                {
                    Retry.TrySetResult();
                    await ReleaseRetry.Task.WaitAsync(cancellationToken);
                }
            }
            return result;
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("course_point_end") && Discovered.TrySetResult())
                await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
