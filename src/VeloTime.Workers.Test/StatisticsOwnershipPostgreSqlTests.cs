using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Workers.Test;

public sealed class PostgreSqlStatisticsFactAttribute : FactAttribute
{
    public PostgreSqlStatisticsFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_STATISTICS_POSTGRES")))
            Skip = "Set VELOTIME_TEST_STATISTICS_POSTGRES to run PostgreSQL statistics ownership tests.";
    }
}

public sealed class StatisticsOwnershipPostgreSqlTests : IAsyncLifetime
{
    private readonly string databaseName = $"statistics_ownership_{Guid.NewGuid():N}";
    private readonly Guid configId = Guid.NewGuid();
    private readonly Guid itemId = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private string connectionString = "";
    private string adminConnectionString = "";

    public async Task InitializeAsync()
    {
        adminConnectionString = Environment.GetEnvironmentVariable("VELOTIME_TEST_STATISTICS_POSTGRES") ?? "";
        if (string.IsNullOrWhiteSpace(adminConnectionString)) return;
        await ExecuteAsync(adminConnectionString, $"CREATE DATABASE \"{databaseName}\"");
        connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
        await using var storage = CreateStorage();
        await storage.Database.EnsureCreatedAsync();
        storage.Add(new SimpleStatisticsItemConfig
        {
            Id = configId, StatisticsItemId = itemId,
            StatisticsItem = new StatisticsItem { Id = itemId, Name = "Lap", Distance = 100 }
        });
        await storage.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            await ExecuteAsync(adminConnectionString, $"DROP DATABASE \"{databaseName}\" WITH (FORCE)");
    }

    [PostgreSqlStatisticsFact]
    public async Task ConcurrentTranspondersCreateOneProfileWithoutOverwritingSettings()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ownerId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readCount = 0;
        var reader = new DelegateReader(async (transponderId, cancellationToken) =>
        {
            if (Interlocked.Increment(ref readCount) == 2) bothRead.SetResult();
            await bothRead.Task.WaitAsync(cancellationToken);
            return [Owner(transponderId, ownerId)];
        });
        await using var first = CreateStorage();
        await using var second = CreateStorage();
        await Task.WhenAll(CreateAsync(first, reader, firstId, timeout.Token), CreateAsync(second, reader, secondId, timeout.Token));

        await using var verify = CreateStorage();
        var profile = Assert.Single(await verify.Set<StatsProfile>().ToListAsync(timeout.Token));
        Assert.Equal(ownerId, profile.UserId);
        Assert.Equal($"Rider {ownerId:D}", profile.ProfileName);
        Assert.True(profile.IsPublic);
        Assert.Equal(2, await verify.Set<StatisticsEntry>().CountAsync(entry => entry.StatsProfileUserId == ownerId, timeout.Token));

        profile.ProfileName = "Custom name";
        profile.IsPublic = false;
        await verify.SaveChangesAsync(timeout.Token);
        var replay = new StatisticsOwnershipService(first, new DelegateReader((id, _) =>
            Task.FromResult<IReadOnlyList<TransponderOwnershipDTO>>([Owner(id, ownerId)])));
        Assert.Equal(0, await replay.ReconcileAsync(Change(firstId, ownerId), timeout.Token));
        verify.ChangeTracker.Clear();
        profile = await verify.Set<StatsProfile>().SingleAsync(timeout.Token);
        Assert.Equal("Custom name", profile.ProfileName);
        Assert.False(profile.IsPublic);
    }

    [PostgreSqlStatisticsFact]
    public async Task ReconciliationWaitsForCreationThenUpdatesItsCommittedEntry()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var transponderId = Guid.NewGuid();
        var oldOwner = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var creationRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCreation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconciliationRead = false;
        var creationReader = new DelegateReader(async (id, cancellationToken) =>
        {
            creationRead.SetResult();
            await releaseCreation.Task.WaitAsync(cancellationToken);
            return [Owner(id, oldOwner)];
        });
        var reconciliationReader = new DelegateReader((id, _) =>
        {
            reconciliationRead = true;
            return Task.FromResult<IReadOnlyList<TransponderOwnershipDTO>>([Owner(id, newOwner)]);
        });
        await using var creationStorage = CreateStorage();
        await using var reconciliationStorage = CreateStorage();
        var creation = CreateAsync(creationStorage, creationReader, transponderId, timeout.Token);
        await creationRead.Task.WaitAsync(timeout.Token);
        var reconciliation = new StatisticsOwnershipService(reconciliationStorage, reconciliationReader)
            .ReconcileAsync(Change(transponderId, oldOwner), timeout.Token);

        // Verify the second connection is actually waiting on the PostgreSQL advisory lock.
        await WaitForAdvisoryWaitAsync(timeout.Token);
        Assert.False(reconciliationRead);
        releaseCreation.SetResult();
        await creation;
        Assert.Equal(1, await reconciliation);

        await using var verify = CreateStorage();
        Assert.Equal(newOwner, (await verify.Set<StatisticsEntry>().SingleAsync(timeout.Token)).StatsProfileUserId);
    }

    [PostgreSqlStatisticsFact]
    public async Task ReconciliationBeforeCreationStillAssignsNewEntryAndHandlesBatchedReplay()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var transponderId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var reader = new DelegateReader((id, _) => Task.FromResult<IReadOnlyList<TransponderOwnershipDTO>>([Owner(id, ownerId)]));
        await using var storage = CreateStorage();
        var service = new StatisticsOwnershipService(storage, reader);
        Assert.Equal(0, await service.ReconcileAsync(Change(transponderId, ownerId), timeout.Token));
        await CreateAsync(storage, reader, transponderId, timeout.Token);
        storage.AddRange(Enumerable.Range(0, 501).Select(_ => Entry(transponderId)));
        await storage.SaveChangesAsync(timeout.Token);
        Assert.Equal(501, await service.ReconcileAsync(Change(transponderId, ownerId), timeout.Token));
        Assert.Equal(0, await service.ReconcileAsync(Change(transponderId, ownerId), timeout.Token));
        Assert.Equal(502, await storage.Set<StatisticsEntry>().CountAsync(entry => entry.StatsProfileUserId == ownerId, timeout.Token));
    }

    [PostgreSqlStatisticsFact]
    public async Task ProfileEnsureWaitsForProcessorAndPreservesItsCommittedProfile()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ownerId = Guid.NewGuid();
        var transponderId = Guid.NewGuid();
        var gate = new ProfileSaveGate();
        await using var processorStorage = CreateStorage(gate);
        await using var apiStorage = CreateStorage();
        var reader = new DelegateReader((id, _) =>
            Task.FromResult<IReadOnlyList<TransponderOwnershipDTO>>([Owner(id, ownerId)]));
        var creation = CreateAsync(processorStorage, reader, transponderId, timeout.Token);
        try
        {
            await gate.Saved.Task.WaitAsync(timeout.Token);
            var ensure = new StatsProfileCreationService(apiStorage).EnsureAsync(ownerId, "Legacy name", false, timeout.Token);
            await WaitForAdvisoryWaitAsync(timeout.Token);
            gate.Release.TrySetResult();
            await creation;
            var (profile, created) = await ensure;
            Assert.False(created);
            Assert.Equal($"Rider {ownerId:D}", profile.ProfileName);
            Assert.True(profile.IsPublic);
            Assert.Single(await apiStorage.Set<StatsProfile>().AsNoTracking().ToListAsync(timeout.Token));
        }
        finally
        {
            gate.Release.TrySetResult();
            await creation;
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task ProcessorWaitsForProfileEnsureAndPreservesItsCommittedSettings()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ownerId = Guid.NewGuid();
        var transponderId = Guid.NewGuid();
        var gate = new ProfileSaveGate();
        await using var apiStorage = CreateStorage(gate);
        await using var processorStorage = CreateStorage();
        var ensure = new StatsProfileCreationService(apiStorage).EnsureAsync(ownerId, "Private name", false, timeout.Token);
        try
        {
            await gate.Saved.Task.WaitAsync(timeout.Token);
            var reader = new DelegateReader((id, _) =>
                Task.FromResult<IReadOnlyList<TransponderOwnershipDTO>>([Owner(id, ownerId)]));
            var creation = CreateAsync(processorStorage, reader, transponderId, timeout.Token);
            await WaitForAdvisoryWaitAsync(timeout.Token);
            gate.Release.TrySetResult();
            Assert.True((await ensure).Created);
            await creation;
            var profile = Assert.Single(await processorStorage.Set<StatsProfile>().AsNoTracking().ToListAsync(timeout.Token));
            Assert.Equal("Private name", profile.ProfileName);
            Assert.False(profile.IsPublic);
            Assert.Equal(ownerId, (await processorStorage.Set<StatisticsEntry>().SingleAsync(timeout.Token)).StatsProfileUserId);
        }
        finally
        {
            gate.Release.TrySetResult();
            await ensure;
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task ExplicitForeignKeyPreservesExistingMigrationModel()
    {
        await using var storage = CreateStorage();
        Assert.False(storage.Database.HasPendingModelChanges());
    }

    private Task<IReadOnlyList<StatisticsEntry>> CreateAsync(StatisticsDbContext storage,
        ITransponderOwnershipReader reader, Guid transponderId, CancellationToken cancellationToken) =>
        new StatisticsOwnershipService(storage, reader).CreateEntriesAsync(transponderId, _ =>
        {
            var entry = Entry(transponderId);
            storage.Add(entry);
            return Task.FromResult<IEnumerable<StatisticsEntry>>([entry]);
        }, cancellationToken);

    private StatisticsEntry Entry(Guid transponderId) => new()
    {
        Id = Guid.NewGuid(), TransponderId = transponderId, StatisticsItemId = itemId, StatisticsItemConfigId = configId,
        TimeStart = Start, TimeEnd = Start.AddSeconds(10), Duration = TimeSpan.FromSeconds(10), Speed = 10, FacilityId = Guid.NewGuid()
    };

    private static TransponderOwnershipDTO Owner(Guid transponderId, Guid ownerId) =>
        new(Guid.NewGuid(), transponderId, ownerId, new DateTimeOffset(Start), null);

    private static TransponderOwnershipChanged Change(Guid transponderId, Guid ownerId) =>
        new(Guid.NewGuid(), transponderId, [new(null, Owner(transponderId, ownerId))]);

    private StatisticsDbContext CreateStorage(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<StatisticsDbContext>().UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }

    private sealed class ProfileSaveGate : SaveChangesInterceptor
    {
        internal TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            Saved.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private async Task WaitForAdvisoryWaitAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        while (true)
        {
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND wait_event = 'advisory')", connection);
            if (await command.ExecuteScalarAsync(cancellationToken) is true) return;
            await Task.Delay(20, cancellationToken);
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class DelegateReader(
        Func<Guid, CancellationToken, Task<IReadOnlyList<TransponderOwnershipDTO>>> read) : ITransponderOwnershipReader
    {
        public Task<IReadOnlyList<TransponderOwnershipDTO>> ReadAsync(Guid transponderId, CancellationToken cancellationToken) =>
            read(transponderId, cancellationToken);
    }
}
