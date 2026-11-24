using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Test;

public sealed partial class StatisticsServiceTests
{
    [Fact]
    public async Task FixedCompletionReplayKeepsOriginalMeasurementAndInvalidationAfterLateStartAndDefinitionEdits()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var startPoint = Guid.NewGuid();
        var intermediate = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var configuredStart = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), configuredStart, finish, "Fixed", 200);
        database.Storage.AddRange(config,
            CreateSample(rider, configuredStart, intermediate, time, time.AddSeconds(5), facility),
            CreateSample(rider, intermediate, finish, time.AddSeconds(10), time.AddSeconds(20), facility));
        await database.Storage.SaveChangesAsync();
        var original = Assert.Single(await database.Service.ProcessCompletedAsync(
            rider, time.AddSeconds(10), time.AddSeconds(20), intermediate, finish));
        var owner = Guid.NewGuid();
        database.Storage.Add(new StatsProfile { UserId = owner, ProfileName = "Owner" });
        original.StatsProfileUserId = owner;
        original.IsValid = false;
        original.InvalidReason = StatisticsEntryInvalidReason.ManuallyInvalidated;
        original.ManualInvalidationChangedByUserId = owner;
        original.ManualInvalidationChangedAtUtc = time;
        config.StatisticsItem.Distance = 999;
        config.RequiredCoursePointIds = [Guid.NewGuid()];
        config.CoursePointEnd = Guid.NewGuid(); // Retargeting must not hide an unpublished result.
        database.Storage.Add(CreateSample(rider, startPoint, intermediate,
            time.AddSeconds(6), time.AddSeconds(9), Guid.NewGuid()));
        await database.Storage.SaveChangesAsync();
        database.Storage.ChangeTracker.Clear();

        var replay = Assert.Single(await database.Service.ProcessCompletedAsync(
            rider, time.AddSeconds(10), time.AddSeconds(20), intermediate, finish));

        Assert.Equal(original.Id, replay.Id);
        Assert.Equal(original.TimeStart, replay.TimeStart);
        Assert.Equal(original.Speed, replay.Speed);
        Assert.Equal(facility, replay.FacilityId);
        Assert.True(replay.RequiredCoursePointsSatisfied);
        Assert.Equal(StatisticsEntryInvalidReason.ManuallyInvalidated, replay.InvalidReason);
        Assert.Equal(owner, replay.StatsProfileUserId);
        Assert.Single(await database.Storage.Set<StatisticsEntry>().ToListAsync());
    }

    [Fact]
    public async Task EqualFinishDifferentCompletionStartsRemainDistinctAttempts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fixed", 200);
        var facility = Guid.NewGuid();
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        database.Storage.AddRange(config,
            CreateSample(rider, config.CoursePointStart, config.CoursePointEnd, time, time.AddSeconds(20), facility),
            CreateSample(rider, config.CoursePointStart, config.CoursePointEnd, time.AddSeconds(1), time.AddSeconds(20), facility));
        await database.Storage.SaveChangesAsync();

        var first = Assert.Single(await database.Service.ProcessCompletedAsync(
            rider, time, time.AddSeconds(20), config.CoursePointStart, config.CoursePointEnd));
        var second = Assert.Single(await database.Service.ProcessCompletedAsync(
            rider, time.AddSeconds(1), time.AddSeconds(20), config.CoursePointStart, config.CoursePointEnd));

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.TimeStart, second.TimeStart);
        Assert.Equal(2, await database.Storage.Set<StatisticsEntry>().CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultiReplayKeepsOriginalWindowAfterLateParentsAndRepetitionEdits(bool requiredPoints)
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var parent = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Parent", 100);
        if (requiredPoints) parent.RequiredCoursePointIds = [Guid.NewGuid()];
        var multi = CreateMultiConfig(parent, 2, "Double", 200);
        var otherParent = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Other", 100);
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var facility = Guid.NewGuid();
        var earlier = CreateEntry(rider, parent, time, time.AddSeconds(10), facility);
        var trigger = CreateEntry(rider, parent, time.AddSeconds(10), time.AddSeconds(20), facility);
        database.Storage.AddRange(parent, otherParent, multi, earlier, trigger);
        await database.Storage.SaveChangesAsync();
        await AddParentReceipt(database.Storage, earlier);
        await AddParentReceipt(database.Storage, trigger);
        Assert.Empty(await database.Service.ProcessEntryAsync(rider, parent.StatisticsItemId, parent.Id,
            earlier.TimeStart, earlier.TimeEnd, entryId: earlier.Id));
        var original = Assert.Single(await database.Service.ProcessEntryAsync(
            rider, parent.StatisticsItemId, parent.Id, time.AddSeconds(10), time.AddSeconds(20)));
        database.Storage.Add(CreateEntry(rider, parent, time.AddSeconds(11), time.AddSeconds(19), facility));
        multi.Repetitions = 10;
        multi.StatisticsItem.Distance = 999;
        multi.ParentConfigId = otherParent.Id;
        multi.ParentConfig = otherParent;
        await database.Storage.SaveChangesAsync();
        database.Storage.ChangeTracker.Clear();

        var replay = Assert.Single(await database.Service.ProcessEntryAsync(
            rider, parent.StatisticsItemId, parent.Id, time.AddSeconds(10), time.AddSeconds(20)));

        Assert.Equal(original.Id, replay.Id);
        Assert.Equal(original.TimeStart, replay.TimeStart);
        Assert.Equal(original.TimeEnd, replay.TimeEnd);
        Assert.Equal(original.Speed, replay.Speed);
        Assert.Equal(original.IsValid, replay.IsValid);
        Assert.Single(await database.Storage.Set<StatisticsEntry>().Where(e => e.StatisticsItemConfigId == multi.Id).ToListAsync());
    }

    [Fact]
    public async Task MultiEqualFinishDifferentTriggerStartsDoNotCoalesceAndAmbiguousParentsFailExplicitly()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var parent = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Parent", 100);
        var multi = CreateMultiConfig(parent, 1, "Single", 100);
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var facility = Guid.NewGuid();
        var firstParent = CreateEntry(rider, parent, time, time.AddSeconds(20), facility);
        var secondParent = CreateEntry(rider, parent, time.AddSeconds(1), time.AddSeconds(20), facility);
        database.Storage.AddRange(parent, multi, firstParent, secondParent);
        await database.Storage.SaveChangesAsync();
        await AddParentReceipt(database.Storage, firstParent);
        await AddParentReceipt(database.Storage, secondParent);

        var first = Assert.Single(await database.Service.ProcessEntryAsync(
            rider, parent.StatisticsItemId, parent.Id, time, time.AddSeconds(20)));
        var second = Assert.Single(await database.Service.ProcessEntryAsync(
            rider, parent.StatisticsItemId, parent.Id, time.AddSeconds(1), time.AddSeconds(20)));
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await database.Storage.Set<StatisticsEntry>().CountAsync(e => e.StatisticsItemConfigId == multi.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessEntryAsync(
            rider, Guid.NewGuid(), parent.Id, time, time.AddSeconds(20)));

        database.Storage.Add(CreateEntry(rider, parent, time, time.AddSeconds(20), Guid.NewGuid()));
        await database.Storage.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessEntryAsync(
            rider, parent.StatisticsItemId, parent.Id, time, time.AddSeconds(20)));
        Assert.Equal(2, await database.Storage.Set<StatisticsEntry>().CountAsync(e => e.StatisticsItemConfigId == multi.Id));
    }

    [Fact]
    public async Task CompletionWithIndistinguishableTimingSourcesFailsInsteadOfMergingAttempts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fixed", 200);
        var facility = Guid.NewGuid();
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        Sample Source() => new()
        {
            Id = Guid.NewGuid(), TransponderId = rider, FacilityId = facility,
            CoursePointStartId = config.CoursePointStart, CoursePointEndId = config.CoursePointEnd,
            TimeStart = time, TimeEnd = time.AddSeconds(10), Duration = TimeSpan.FromSeconds(10),
            Distance = 200, Speed = 20, TimingPointStartId = Guid.NewGuid(), TimingPointEndId = Guid.NewGuid()
        };
        database.Storage.AddRange(config, Source(), Source());
        await database.Storage.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessCompletedAsync(
            rider, time, time.AddSeconds(10), config.CoursePointStart, config.CoursePointEnd));
        Assert.Empty(await database.Storage.Set<StatisticsEntry>().ToListAsync());
    }

    [Fact]
    public async Task SampleIdentitySeparatesEqualPayloadSourcesAndIdlessReplayAliasesUniqueSource()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var start = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), start, finish, "Fixed", 100);
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var firstSource = CreateSample(rider, start, finish, time, time.AddSeconds(10), facility);
        var secondSource = CreateSample(rider, start, finish, time, time.AddSeconds(10), facility);
        database.Storage.AddRange(config, firstSource);
        await database.Storage.SaveChangesAsync();

        var first = Assert.Single(await database.Service.ProcessCompletedAsync(rider, time, time.AddSeconds(10),
            start, finish, default, firstSource.Id, firstSource.Distance));
        var legacyAlias = Assert.Single(await database.Service.ProcessCompletedAsync(rider, time, time.AddSeconds(10),
            start, finish, default, null, firstSource.Distance));
        database.Storage.Add(secondSource);
        await database.Storage.SaveChangesAsync();
        var second = Assert.Single(await database.Service.ProcessCompletedAsync(rider, time, time.AddSeconds(10),
            start, finish, default, secondSource.Id, secondSource.Distance));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.Id, legacyAlias.Id);
        Assert.Equal(2, await database.Storage.Set<StatisticsEntry>().CountAsync());
    }

    [Fact]
    public async Task RequiredPointReplayReaderDoesNotCollapseDifferentSourceSamplesAtSameFinishAndFacility()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var start = Guid.NewGuid();
        var required = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), start, finish, "Required", 100);
        config.RequiredCoursePointIds = [required];
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var firstSource = CreateSample(rider, required, finish, time.AddSeconds(10), time.AddSeconds(20), facility);
        var secondSource = CreateSample(rider, required, finish, time.AddSeconds(10), time.AddSeconds(20), facility);
        database.Storage.AddRange(config,
            CreateSample(rider, start, required, time, time.AddSeconds(10), facility),
            firstSource, secondSource);
        await database.Storage.SaveChangesAsync();

        var first = Assert.Single(await database.Service.ProcessCompletedAsync(rider, firstSource.TimeStart,
            firstSource.TimeEnd, required, finish, default, firstSource.Id, firstSource.Distance));
        var second = Assert.Single(await database.Service.ProcessCompletedAsync(rider, secondSource.TimeStart,
            secondSource.TimeEnd, required, finish, default, secondSource.Id, secondSource.Distance));

        Assert.NotEqual(first.Id, second.Id);
        Assert.True(first.RequiredCoursePointsSatisfied);
        Assert.True(second.RequiredCoursePointsSatisfied);
        Assert.Equal(2, await database.Storage.Set<StatisticsEntry>().CountAsync());
    }

    [Fact]
    public async Task IdlessCompletionRequiresUniqueSourceAndExplicitIdentityValidatesEveryPayloadField()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var start = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), start, finish, "Fixed", 100);
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var source = CreateSample(rider, start, finish, time, time.AddSeconds(10), facility);
        database.Storage.AddRange(config, source);
        await database.Storage.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessCompletedAsync(rider,
            time, time.AddSeconds(10), start, finish, default, Guid.NewGuid(), source.Distance));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessCompletedAsync(rider,
            time, time.AddSeconds(10), start, finish, default, Guid.Empty, source.Distance));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessCompletedAsync(rider,
            time, time.AddSeconds(10), start, finish, default, source.Id, source.Distance + 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessCompletedAsync(rider,
            time.AddSeconds(1), time.AddSeconds(10), start, finish, default, source.Id, source.Distance));

        database.Storage.Add(CreateSample(rider, start, finish, time, time.AddSeconds(10), facility));
        await database.Storage.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessCompletedAsync(rider,
            time, time.AddSeconds(10), start, finish, default, null, source.Distance));
        Assert.Empty(await database.Storage.Set<StatisticsEntry>().ToListAsync());
    }

    [Fact]
    public async Task ParentEntryIdentitySeparatesIdenticalPayloadsAndIdlessAmbiguityFailsClosed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var parent = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Parent", 100);
        var multi = CreateMultiConfig(parent, 1, "Single", 100);
        var start = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddSeconds(10);
        var firstParent = CreateEntry(rider, parent, start, end, Guid.NewGuid());
        var secondParent = CreateEntry(rider, parent, start, end, Guid.NewGuid());
        database.Storage.AddRange(parent, multi, firstParent, secondParent);
        await database.Storage.SaveChangesAsync();
        await AddParentReceipt(database.Storage, firstParent);
        await AddParentReceipt(database.Storage, secondParent);

        var first = Assert.Single(await database.Service.ProcessEntryAsync(rider, parent.StatisticsItemId,
            parent.Id, start, end, default, firstParent.Id));
        var second = Assert.Single(await database.Service.ProcessEntryAsync(rider, parent.StatisticsItemId,
            parent.Id, start, end, default, secondParent.Id));

        Assert.NotEqual(first.Id, second.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessEntryAsync(rider,
            parent.StatisticsItemId, parent.Id, start, end));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessEntryAsync(rider,
            parent.StatisticsItemId, parent.Id, start, end, default, Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessEntryAsync(rider,
            Guid.NewGuid(), parent.Id, start, end, default, firstParent.Id));
        Assert.Equal(2, await database.Storage.Set<StatisticsEntry>()
            .CountAsync(entry => entry.StatisticsItemConfigId == multi.Id));
    }

    [Fact]
    public async Task PersistedTupleV1CompletionIdentityBlocksReplayUntilMaintenanceReconciliation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var start = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), start, finish, "Fixed", 100);
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var source = CreateSample(rider, start, finish, time, time.AddSeconds(10), facility);
        var legacyId = LegacyId($"statistics-completion-v1|{config.Id:D}|{rider:D}|{time.Ticks}|{time.AddSeconds(10).Ticks}|{start:D}|{finish:D}");
        database.Storage.AddRange(config, source, new StatisticsEntry
        {
            Id = legacyId, TransponderId = rider, StatisticsItemId = config.StatisticsItemId,
            StatisticsItem = config.StatisticsItem, StatisticsItemConfig = config,
            StatisticsItemConfigId = config.Id, TimeStart = time, TimeEnd = time.AddSeconds(10),
            Duration = TimeSpan.FromSeconds(10), Speed = 10, FacilityId = facility
        });
        await database.Storage.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessCompletedAsync(
            rider, time, time.AddSeconds(10), start, finish, default, source.Id, source.Distance));
        Assert.Contains("tuple-v1", error.Message);
        Assert.Single(await database.Storage.Set<StatisticsEntry>().ToListAsync());
    }

    [Fact]
    public async Task PersistedTupleV1ParentIdentityBlocksReplayUntilMaintenanceReconciliation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var parent = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Parent", 100);
        var multi = CreateMultiConfig(parent, 1, "Single", 100);
        var start = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddSeconds(10);
        var source = CreateEntry(rider, parent, start, end, Guid.NewGuid());
        database.Storage.AddRange(parent, multi, source);
        await database.Storage.SaveChangesAsync();
        await AddParentReceipt(database.Storage, source);
        var legacyId = LegacyId($"statistics-parent-v1|{multi.Id:D}|{rider:D}|{parent.StatisticsItemId:D}|{parent.Id:D}|{start.Ticks}|{end.Ticks}");
        database.Storage.Add(new StatisticsEntry
        {
            Id = legacyId, TransponderId = rider, StatisticsItemId = multi.StatisticsItemId,
            StatisticsItem = multi.StatisticsItem, StatisticsItemConfig = multi,
            StatisticsItemConfigId = multi.Id, TimeStart = start, TimeEnd = end,
            Duration = TimeSpan.FromSeconds(10), Speed = 10, FacilityId = source.FacilityId
        });
        await database.Storage.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessEntryAsync(
            rider, parent.StatisticsItemId, parent.Id, start, end, default, source.Id));
        Assert.Contains("tuple-v1", error.Message);
        Assert.Equal(2, await database.Storage.Set<StatisticsEntry>().CountAsync());
    }

    private static Guid LegacyId(string source) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(source)).AsSpan(0, 16));

    private static async Task AddParentReceipt(
        VeloTime.Module.Statistics.Storage.StatisticsDbContext storage, StatisticsEntry entry)
    {
        storage.Add(new EntryCreatedOutbox
        {
            EntryId = entry.Id, TransponderId = entry.TransponderId,
            StatisticsItemId = entry.StatisticsItemId, ConfigItemId = entry.StatisticsItemConfigId,
            TimeStart = entry.TimeStart, TimeEnd = entry.TimeEnd, Single = true,
            MultiDecisionVersion = 1
        });
        await storage.SaveChangesAsync();
    }
}
