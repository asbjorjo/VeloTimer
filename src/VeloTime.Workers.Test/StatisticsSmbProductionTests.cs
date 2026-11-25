using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Messages;
using Assert = Xunit.Assert;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsSmbOutboxProofTests
{
    [PostgreSqlStatisticsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionHandlersPreserveBothStreamsAcrossReverseDeliveryDuplicatesAndLostAck(bool lostAck)
    {
        var (simple, multi) = await ProductionDefinitions();
        var rider = Guid.NewGuid();
        var first = Timing(rider);
        var second = Timing(rider); // identical measurement, distinct timing source identity
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Produce(first, simple)));
        await Produce(second, simple);
        List<SampleCompleteOutbox> samples;
        await using (var db = Db())
        {
            samples = await db.Set<SampleCompleteOutbox>().OrderBy(r => r.Id).ToListAsync();
            Assert.Equal(2, samples.Count);
            Assert.NotNull(samples[0].SmbEnqueuedAtUtc);
            Assert.Null(samples[1].SmbEnqueuedAtUtc);
        }
        Assert.Equal(1, await Count());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Consume(SampleWire(samples[1])));
        var ackHeld = Signal();
        var releaseAck = Signal();
        var attempts = 0;
        sender.FailAfterAccept = lostAck;
        sender.OnAccepted = async wire =>
        {
            await Consume(wire); // real handler, geometry, ownership and decisions, fresh scoped transaction
            if (WireId(wire) == samples[0].SampleId && Interlocked.Increment(ref attempts) == 1)
            {
                ackHeld.TrySetResult();
                await releaseAck.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
        };
        sender.Available = true;
        try
        {
            await ackHeld.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(150);
            Assert.Single(sender.Accepted);
            Assert.False(await DeliveryStatus(samples[0].SampleId, nameof(SampleComplete)));
            Assert.Equal(3, await Count()); // sample successor AND first Entry head
        }
        finally { releaseAck.TrySetResult(); }
        if (lostAck)
        {
            await Until(() => Task.FromResult(sender.Accepted.Count >= 2));
            await Bus.Stop();
            Assert.All(sender.Accepted, w => Assert.Equal(samples[0].SampleId, WireId(w)));
            var old = provider!;
            await old.DisposeAsync(); // retained database; new lease identity/poller
            provider = null;
            master = null;
            sender.FailAfterAccept = false;
            await StartProofProvider();
        }
        await Until(async () => await DeliveredCount() == 6);
        await Bus.Stop();
        var accepted = sender.Accepted.ToArray();
        foreach (var name in new[] { nameof(SampleComplete), nameof(EntryCreated) })
        {
            var ids = accepted.Where(w => (string)w.ApplicationProperties["MessageName"] == name)
                .Select(WireId).Distinct().ToArray();
            await using var db = Db();
            var expected = name == nameof(SampleComplete)
                ? await db.Set<SampleCompleteOutbox>().OrderBy(r => r.Id).Select(r => r.SampleId).ToArrayAsync()
                : await db.Set<EntryCreatedOutbox>().OrderBy(r => r.Id).Select(r => r.EntryId).ToArrayAsync();
            Assert.Equal(expected, ids);
        }
        await Task.WhenAll(accepted.Reverse().Select(Consume)); // concurrent delayed duplicates
        await using (var db = Db())
        {
            Assert.Equal(4, await db.Set<StatisticsEntry>().CountAsync());
            Assert.Equal(2, await db.Set<MultiCreationDecision>().CountAsync());
            Assert.Equal(2, await db.Set<StatisticsEntry>().CountAsync(e => e.StatisticsItemConfigId == multi.Id));
            Assert.All(await db.Set<SampleCompleteOutbox>().ToListAsync(), r => Assert.NotNull(r.EntriesCreatedAtUtc));
            Assert.All(await db.Set<EntryCreatedOutbox>().ToListAsync(), r =>
            {
                Assert.NotNull(r.SmbEnqueuedAtUtc);
                Assert.NotNull(r.MultiDecisionsCreatedAtUtc); // includes non-Single
                Assert.Null(r.DeliveredAtUtc); // legacy dispatch evidence is never forged
            });
            await StatisticsProcessorStartup.ValidateAsync(db, default);
        }
        Assert.Equal(6, await Count());
        Assert.Contains(accepted, w => w.ApplicationProperties.TryGetValue("Single", out var flag) && flag is true);
        Assert.Contains(accepted, w => (string)w.ApplicationProperties["MessageName"] == nameof(EntryCreated) &&
            !w.ApplicationProperties.ContainsKey("Single"));
    }

    [PostgreSqlStatisticsTheory]
    [InlineData("enqueue")]
    [InlineData("commit")]
    public async Task ProductionTransactionsRollbackReceiptEffectsCompletionAndSuccessorTogether(string failure)
    {
        var (simple, _) = await ProductionDefinitions();
        var first = Timing(Guid.NewGuid());
        if (failure == "enqueue") await RejectEnqueue(true);
        else rejectCommit.Enabled = true;
        await Assert.ThrowsAnyAsync<Exception>(() => Produce(first, simple));
        rejectCommit.Enabled = false;
        if (failure == "enqueue") await RejectEnqueue(false);
        await using (var db = Db())
        {
            Assert.Empty(await db.Set<Sample>().ToListAsync());
            Assert.Empty(await db.Set<SampleCompleteOutbox>().ToListAsync());
        }
        Assert.Equal(0, await Count());
        await Produce(first, simple);
        await Produce(Timing(first.TransponderId), simple);
        sender.Available = true;
        await Until(async () => await DeliveredCount() == 1);
        await Bus.Stop(); // crash/broker fail-stop after ack, before domain handling
        var wire = Assert.Single(sender.Accepted);
        if (failure == "enqueue") await RejectEnqueue(true);
        else rejectCommit.Enabled = true;
        await Assert.ThrowsAnyAsync<Exception>(() => Consume(wire));
        rejectCommit.Enabled = false;
        if (failure == "enqueue") await RejectEnqueue(false);
        await using (var db = Db())
        {
            Assert.Empty(await db.Set<StatisticsEntry>().ToListAsync());
            Assert.Empty(await db.Set<EntryCreatedOutbox>().ToListAsync());
            Assert.Empty(await db.Set<MultiCreationDecision>().ToListAsync());
            Assert.All(await db.Set<SampleCompleteOutbox>().ToListAsync(), r => Assert.Null(r.EntriesCreatedAtUtc));
            Assert.Single(await db.Set<SampleCompleteOutbox>().Where(r => r.SmbEnqueuedAtUtc != null).ToListAsync());
        }
        Assert.Equal(1, await Count());
        // Explicit redrive of the same accepted head, not a second enqueue.
        await Consume(wire);
        Assert.Equal(3, await Count());
        await Consume(wire);
        Assert.Equal(3, await Count());
    }

    [PostgreSqlStatisticsFact]
    public async Task ProductionEmptySampleAndEmptyOrNoResultMultiAreFinalAndOtherRiderCanProgress()
    {
        var points = ProductionSimple();
        var rider = Guid.NewGuid();
        await Produce(Timing(rider), points); // no definitions
        sender.OnAccepted = Consume;
        sender.Available = true;
        await Until(async () => await DeliveredCount() == 1);
        var empty = sender.Accepted.Single();
        var (simple, multi) = await ProductionDefinitions(points, repetitions: 2);
        await Consume(empty); // adding a definition cannot reopen an empty sample
        await using (var db = Db()) Assert.Empty(await db.Set<StatisticsEntry>().ToListAsync());
        await Produce(Timing(rider), simple);
        await Until(async () => await DeliveredCount() == 3); // one Single, insufficient parents
        Guid parent;
        await using (var db = Db())
        {
            var decision = await db.Set<MultiCreationDecision>().SingleAsync();
            parent = decision.ParentEntryId;
            Assert.Null(decision.ResultEntryId);
            (await db.Set<MultiStatisticsItemConfig>().SingleAsync()).Repetitions = 1;
            await db.SaveChangesAsync();
        }
        await Consume(sender.Accepted.Single(w => WireId(w) == parent));
        await using (var db = Db()) Assert.Null((await db.Set<MultiCreationDecision>().SingleAsync()).ResultEntryId);
        // The Entry stream is independently blocked by an unconsumed Single. A later
        // sample (and another rider) still reaches the broker and produces its entries.
        sender.OnAccepted = null;
        await Produce(Timing(rider), simple);
        await Until(async () => await DeliveredCount() == 4);
        await Consume(sender.Accepted.Last());
        await Until(async () => await DeliveredCount() == 5);
        await Produce(Timing(rider), simple);
        await Until(async () => await DeliveredCount() == 6);
        await Consume(sender.Accepted.Last());
        await Produce(Timing(Guid.NewGuid()), simple);
        await Until(async () => await DeliveredCount() == 7);
        await using (var db = Db())
        {
            Assert.Equal(4, await db.Set<SampleCompleteOutbox>().CountAsync(r => r.EntriesCreatedAtUtc != null));
            Assert.Equal(2, await db.Set<EntryCreatedOutbox>().CountAsync(r => r.MultiDecisionsCreatedAtUtc == null));
            Assert.Single(await db.Set<EntryCreatedOutbox>().Where(r =>
                r.MultiDecisionsCreatedAtUtc == null && r.SmbEnqueuedAtUtc != null).ToListAsync());
            Assert.Single(await db.Set<MultiCreationDecision>().ToListAsync());
            var later = await db.Set<EntryCreatedOutbox>().Where(r => r.MultiDecisionsCreatedAtUtc == null)
                .OrderByDescending(r => r.Id).FirstAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => Consume(EntryWire(later)));
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task ProductionCompletedDuplicatesValidateHeadersAndPayloadAfterSourceDeletionAndSmbCleanup()
    {
        var (simple, _) = await ProductionDefinitions();
        await Produce(Timing(Guid.NewGuid()), simple);
        sender.OnAccepted = Consume;
        sender.Available = true;
        await Until(async () => await DeliveredCount() == 3);
        await Bus.Stop();
        var wires = sender.Accepted.ToArray();
        foreach (var original in wires)
        {
            foreach (var change in new Action<ServiceBusMessage>[]
            {
                w => w.ApplicationProperties["MessageName"] = "wrong",
                w => w.ApplicationProperties[ReceiptHeader] = Guid.NewGuid().ToString(),
                w => w.ApplicationProperties["Single"] = false,
                w => w.Body = BinaryData.FromString("{}")
            })
            {
                var invalid = CopyWire(original);
                change(invalid);
                await Assert.ThrowsAsync<InvalidOperationException>(() => Consume(invalid,
                    new ProductionEmptyOwnershipReader(), sample: original.ApplicationProperties["MessageName"] is nameof(SampleComplete)));
            }
            IEnumerable<object> invalidBodies;
            if (original.ApplicationProperties["MessageName"] is nameof(SampleComplete))
            {
                var message = JsonSerializer.Deserialize<SampleComplete>(original.Body.ToString(), JsonOptions)!;
                invalidBodies = new[] { message with { SampleId = null }, message with { SampleId = Guid.Empty },
                    message with { SampleId = Guid.NewGuid() }, message with { Distance = 251 },
                    message with { CoursePointStart = Guid.NewGuid() }, message with { TimeEnd = message.TimeEnd.AddSeconds(1) } };
            }
            else
            {
                var message = JsonSerializer.Deserialize<EntryCreated>(original.Body.ToString(), JsonOptions)!;
                invalidBodies = new[] { message with { EntryId = null }, message with { EntryId = Guid.Empty },
                    message with { EntryId = Guid.NewGuid() }, message with { StatisticsItemId = Guid.NewGuid() },
                    message with { ConfigItemId = Guid.NewGuid() }, message with { TimeEnd = message.TimeEnd.AddSeconds(1) } };
            }
            foreach (var body in invalidBodies)
            {
                var invalid = CopyWire(original);
                invalid.Body = BinaryData.FromString(JsonSerializer.Serialize(body, body.GetType(), JsonOptions));
                await Assert.ThrowsAsync<InvalidOperationException>(() => Consume(invalid));
            }
        }
        await Sql(connection, $"DELETE FROM {Schema}.smb_outbox"); // deliberate completed SMB cleanup
        await using (var db = Db())
        {
            await db.Set<StatisticsEntry>().ExecuteDeleteAsync();
            await db.Set<Sample>().ExecuteDeleteAsync();
            await db.Set<StatisticsItemConfig>().ExecuteDeleteAsync();
        }
        foreach (var wire in wires.Reverse()) await Consume(wire);
        Assert.Equal(0, await Count()); // durable markers forbid automatic republish
        await using (var db = Db())
        {
            Assert.Empty(await db.Set<StatisticsEntry>().ToListAsync());
            Assert.Equal(2, await db.Set<EntryCreatedOutbox>().CountAsync());
            Assert.Single(await db.Set<MultiCreationDecision>().ToListAsync());
            await db.Set<SampleCompleteOutbox>().ExecuteDeleteAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Consume(wires.Single(w =>
            w.ApplicationProperties["MessageName"] is nameof(SampleComplete))));
    }

    [PostgreSqlStatisticsFact]
    public async Task ProductionStartupRejectsUnreleasedAndLegacyStateAndResolverDoesNotSilentlyAbort()
    {
        await using var db = Db();
        await StatisticsProcessorStartup.ValidateAsync(db, default);
        var receipt = Receipt();
        db.Add(receipt);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => StatisticsProcessorStartup.ValidateAsync(db, default));
        await using (var scope = provider!.CreateAsyncScope())
        {
            var scoped = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            await using var tx = await scoped.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<StatisticsHeadRelease>().ReleaseAsync(receipt.TransponderId, default);
            await tx.CommitAsync();
        }
        await StatisticsProcessorStartup.ValidateAsync(db, default);
        var resolver = provider!.GetRequiredService<SlimMessageBus.Host.Outbox.PostgreSql.Configuration.PostgreSqlOutboxSettings>().MessageTypeResolver;
        Assert.Throws<InvalidOperationException>(() => resolver.ToType("Missing.Removed.Message, Missing.Assembly"));
        await Sql(connection, $"DELETE FROM {Schema}.smb_outbox");
        await Assert.ThrowsAsync<InvalidOperationException>(() => StatisticsProcessorStartup.ValidateAsync(db, default));
    }

    [PostgreSqlStatisticsTheory]
    [InlineData("enqueue")]
    [InlineData("commit")]
    public async Task ProductionMultiFailureRollsBackDecisionsDerivedReceiptAndNonSingleRelease(string failure)
    {
        var (simple, _) = await ProductionDefinitions();
        await Produce(Timing(Guid.NewGuid()), simple);
        sender.Available = true;
        await Until(async () => await DeliveredCount() == 1);
        await Bus.Stop();
        await Consume(sender.Accepted.Single());
        await Bus.Start();
        await Until(async () => await DeliveredCount() == 2);
        await Bus.Stop();
        var single = sender.Accepted.Single(w => w.ApplicationProperties.ContainsKey("Single"));
        if (failure == "enqueue") await RejectEnqueue(true);
        else rejectCommit.Enabled = true;
        await Assert.ThrowsAnyAsync<Exception>(() => Consume(single));
        rejectCommit.Enabled = false;
        if (failure == "enqueue") await RejectEnqueue(false);
        await using (var db = Db())
        {
            Assert.Single(await db.Set<StatisticsEntry>().ToListAsync());
            Assert.Single(await db.Set<EntryCreatedOutbox>().ToListAsync());
            Assert.Empty(await db.Set<MultiCreationDecision>().ToListAsync());
            Assert.Null((await db.Set<EntryCreatedOutbox>().SingleAsync()).MultiDecisionsCreatedAtUtc);
        }
        Assert.Equal(2, await Count());
        await Consume(single);
        Assert.Equal(3, await Count());
        await Consume(single);
        Assert.Equal(3, await Count());
        sender.OnAccepted = Consume;
        await Bus.Start();
        await Until(async () => await DeliveredCount() == 3);
        await using (var db = Db())
        {
            Assert.Single(await db.Set<MultiCreationDecision>().ToListAsync());
            Assert.All(await db.Set<EntryCreatedOutbox>().ToListAsync(), r => Assert.NotNull(r.MultiDecisionsCreatedAtUtc));
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task ProductionOwnershipFailureRollsBackMultipleOutputsAndConcurrentRedrivesAssignExactlyOnce()
    {
        var (simple, _) = await ProductionDefinitions();
        var second = ProductionSimple();
        second.CoursePointStart = simple.CoursePointStart;
        second.CoursePointEnd = simple.CoursePointEnd;
        await ProductionDefinitions(second);
        var rider = Guid.NewGuid();
        await Produce(Timing(rider), simple);
        sender.Available = true;
        await Until(async () => await DeliveredCount() == 1);
        await Bus.Stop();
        var wire = sender.Accepted.Single();
        var owner = Guid.NewGuid();
        var reader = new StatisticsOwnershipTests.Reader
        {
            Owners = [
                new(Guid.NewGuid(), rider, owner, new DateTimeOffset(Start.AddDays(-1)), null),
                new(Guid.NewGuid(), rider, Guid.NewGuid(), new DateTimeOffset(Start.AddDays(-1)), null)]
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Consume(wire, reader));
        await using (var db = Db())
        {
            Assert.Empty(await db.Set<StatisticsEntry>().ToListAsync());
            Assert.Empty(await db.Set<StatsProfile>().ToListAsync());
            Assert.Empty(await db.Set<EntryCreatedOutbox>().ToListAsync());
            Assert.Null((await db.Set<SampleCompleteOutbox>().SingleAsync()).EntriesCreatedAtUtc);
        }
        reader.Owners = [reader.Owners[0]];
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Consume(wire, reader)));
        await using (var db = Db())
        {
            Assert.Equal(2, await db.Set<StatisticsEntry>().CountAsync());
            Assert.All(await db.Set<StatisticsEntry>().ToListAsync(), e => Assert.Equal(owner, e.StatsProfileUserId));
            Assert.Single(await db.Set<StatsProfile>().ToListAsync());
            Assert.Equal(2, await db.Set<EntryCreatedOutbox>().CountAsync());
            Assert.Single(await db.Set<EntryCreatedOutbox>().Where(r => r.SmbEnqueuedAtUtc != null).ToListAsync());
        }
        sender.OnAccepted = w => Consume(w, reader);
        await Bus.Start();
        await Until(async () => await DeliveredCount() == 5);
        await using (var db = Db())
        {
            Assert.Equal(4, await db.Set<StatisticsEntry>().CountAsync());
            Assert.Equal(2, await db.Set<MultiCreationDecision>().CountAsync());
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task ProductionRetriesKeepFrozenRequiredPointOutcomeAndFacilitiesPayloadAfterDefinitionChanges()
    {
        var simple = ProductionSimple();
        simple.CoursePointEnd = simple.CoursePointStart;
        simple.RequiredCoursePointIds = [Guid.NewGuid()];
        simple.RequiredCourseLayoutId = Guid.NewGuid();
        var (_, multi) = await ProductionDefinitions(simple);
        var timing = Timing(Guid.NewGuid());
        Assert.Equal(1, await Produce(timing, simple));
        await Until(async () => await Scalar<int>($"SELECT delivery_attempt FROM {Schema}.smb_outbox") > 0);
        var changed = ProductionSimple();
        changed.StatisticsItem.Distance = 999;
        Assert.Equal(0, await Produce(timing, changed)); // replay cannot re-enrich even while transport fails
        var processedSingle = Signal();
        sender.OnAccepted = async w =>
        {
            await Consume(w);
            if (w.ApplicationProperties.ContainsKey("Single"))
            {
                sender.FailAfterAccept = true;
                processedSingle.TrySetResult();
            }

        };
        sender.Available = true;
        await processedSingle.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Until(() => Task.FromResult(sender.Accepted.Count >= 3));
        await Bus.Stop();
        var singles = sender.Accepted.Where(w => w.ApplicationProperties.ContainsKey("Single")).ToArray();
        Assert.True(singles.Length >= 2);
        Assert.Single(singles.Select(w => w.Body.ToString()).Distinct());
        await using (var db = Db())
        {
            var config = await db.Set<SimpleStatisticsItemConfig>().Include(c => c.StatisticsItem).SingleAsync();
            config.RequiredCoursePointIds = [];
            config.StatisticsItem.Distance = 999;
            (await db.Set<MultiStatisticsItemConfig>().SingleAsync()).Repetitions = 10;
            await db.SaveChangesAsync();
        }
        await Consume(singles[0]); // neither Simple nor Multi is recomputed
        sender.FailAfterAccept = false;
        sender.OnAccepted = Consume;
        await Bus.Start();
        await Until(async () => await DeliveredCount() == 3);
        await using (var db = Db())
        {
            var entries = await db.Set<StatisticsEntry>().ToListAsync();
            Assert.Equal(2, entries.Count);
            Assert.Equal(VeloTime.Module.Statistics.Interface.Data.StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied,
                entries.Single(e => e.StatisticsItemConfigId == simple.Id).InvalidReason);
            Assert.Equal(VeloTime.Module.Statistics.Interface.Data.StatisticsEntryInvalidReason.InvalidContributor,
                entries.Single(e => e.StatisticsItemConfigId == multi.Id).InvalidReason);
            Assert.Equal(250, (await db.Set<SampleCompleteOutbox>().SingleAsync()).Distance);
            Assert.Single(await db.Set<MultiCreationDecision>().ToListAsync());
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task ProductionEmptyMultiSetAndLegacyCompletedRedeliveryNeverDiscoverNewDefinitions()
    {
        var simple = ProductionSimple();
        await using (var db = Db()) { db.Add(simple); await db.SaveChangesAsync(); }
        await Produce(Timing(Guid.NewGuid()), simple);
        sender.OnAccepted = Consume;
        sender.Available = true;
        await Until(async () => await DeliveredCount() == 2);
        await Bus.Stop();
        var single = sender.Accepted.Single(w => w.ApplicationProperties.ContainsKey("Single"));
        await using (var db = Db())
        {
            Assert.Empty(await db.Set<MultiCreationDecision>().ToListAsync());
            Assert.NotNull((await db.Set<EntryCreatedOutbox>().SingleAsync()).MultiDecisionsCreatedAtUtc);
            var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Late Multi", Distance = 500 };
            db.Add(new MultiStatisticsItemConfig { Id = Guid.NewGuid(), StatisticsItem = item,
                ParentConfig = null!, ParentConfigId = simple.Id, Repetitions = 1 });
            await db.SaveChangesAsync();
        }
        await Consume(single);
        // Conservatively reconciled legacy completion: no synthetic enqueue.
        await using (var db = Db())
        {
            await db.Set<EntryCreatedOutbox>().ExecuteUpdateAsync(s => s
                .SetProperty(r => r.SmbEnqueuedAtUtc, (DateTime?)null)
                .SetProperty(r => r.DeliveredAtUtc, DateTime.UtcNow));
            await StatisticsProcessorStartup.ValidateAsync(db, default);
        }
        var legacy = CopyWire(single);
        legacy.ApplicationProperties.Remove(ReceiptHeader);
        await Consume(legacy);
        await using (var db = Db())
        {
            Assert.Single(await db.Set<StatisticsEntry>().ToListAsync());
            Assert.Empty(await db.Set<MultiCreationDecision>().ToListAsync());
            Assert.Null((await db.Set<EntryCreatedOutbox>().SingleAsync()).SmbEnqueuedAtUtc);
        }
        Assert.Equal(2, await Count());
    }

    [PostgreSqlStatisticsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionCancellationAtCommitRollsBackEnqueueAndDomainCompletion(bool consumer)
    {
        var (simple, _) = await ProductionDefinitions();
        var timing = Timing(Guid.NewGuid());
        ServiceBusMessage? wire = null;
        if (consumer)
        {
            await Produce(timing, simple);
            sender.Available = true;
            await Until(async () => await DeliveredCount() == 1);
            await Bus.Stop();
            wire = sender.Accepted.Single();
        }
        using var cts = new CancellationTokenSource();
        rejectCommit.BeforeCommit = cts.Cancel;
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                if (consumer) await Consume(wire!, new ProductionEmptyOwnershipReader(), cts.Token);
                else await Produce(timing, simple, cts.Token);
            });
        }
        finally { rejectCommit.BeforeCommit = null; }
        await using (var db = Db())
        {
            Assert.Empty(await db.Set<StatisticsEntry>().ToListAsync());
            Assert.Empty(await db.Set<EntryCreatedOutbox>().ToListAsync());
            Assert.All(await db.Set<SampleCompleteOutbox>().ToListAsync(), r => Assert.Null(r.EntriesCreatedAtUtc));
            Assert.Equal(consumer ? 1 : 0, await db.Set<Sample>().CountAsync());
        }
        Assert.Equal(consumer ? 1 : 0, await Count());
        if (consumer) await Consume(wire!);
        else await Produce(timing, simple);
        Assert.Equal(consumer ? 2 : 1, await Count());
    }

    [PostgreSqlStatisticsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionInsertAndEmptyCompletionRaceReleaseExactlyOneSuccessor(bool insertFirst)
    {
        var simple = ProductionSimple(); // intentionally zero eligible definitions
        var first = Timing(Guid.NewGuid());
        await Produce(first, simple);
        sender.Available = true;
        await Until(async () => await DeliveredCount() == 1);
        await Bus.Stop();
        var wire = sender.Accepted.Single();
        var held = Signal();
        var release = Signal();
        StatisticsDbContext? target = null;
        rejectCommit.BeforeCommitAsync = async db =>
        {
            if (!ReferenceEquals(db, target)) return;
            held.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
        };
        var second = Timing(first.TransponderId);
        Task winner = insertFirst
            ? Produce(second, simple, configure: db => target = db)
            : Consume(wire, new ProductionEmptyOwnershipReader(), configure: db => target = db);
        await held.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Task contender = insertFirst ? Consume(wire) : Produce(second, simple);
        try
        {
            await Until(async () => await Scalar<bool>("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                               WHERE datname = current_database() AND wait_event = 'advisory')
                """));
            Assert.False(contender.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(winner, contender);
        rejectCommit.BeforeCommitAsync = null;
        Assert.Equal(2, await Count());
        await using (var db = Db())
        {
            var receipts = await db.Set<SampleCompleteOutbox>().OrderBy(r => r.Id).ToListAsync();
            Assert.Equal(2, receipts.Count);
            Assert.NotNull(receipts[0].EntriesCreatedAtUtc);
            Assert.Null(receipts[1].EntriesCreatedAtUtc);
            Assert.All(receipts, r => Assert.NotNull(r.SmbEnqueuedAtUtc));
            Assert.Empty(await db.Set<StatisticsEntry>().ToListAsync());
        }
        await Bus.Start();
        await Until(async () => await DeliveredCount() == 2);
        await Consume(sender.Accepted.Last());
        Assert.Equal(2, await Count());
    }

    [PostgreSqlStatisticsFact]
    public async Task ProductionExplicitStartupStartsIdlePollerWithoutInboundTraffic()
    {
        await Bus.Stop();
        await Produce(Timing(Guid.NewGuid()), ProductionSimple());
        Assert.Empty(sender.Accepted);
        sender.Available = true;
        await new ProcessorTestHost(provider!).StartStatisticsProcessorAsync();
        await Until(async () => await DeliveredCount() == 1);
        Assert.Single(sender.Accepted);
    }

    private sealed class ProcessorTestHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services => services;
        public void Dispose() { }
        public Task StartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [PostgreSqlStatisticsFact]
    public async Task ProductionSampleReplayKeepsCompleteFacilitiesSnapshotAfterAssociationChanges()
    {
        var simple = ProductionSimple();
        var timing = Timing(Guid.NewGuid());
        var original = new CoursePointDistance
        {
            CoursePointStartId = simple.CoursePointStart, CoursePointEndId = simple.CoursePointEnd,
            Distance = 100, FacilityId = Guid.NewGuid(), CourseLayoutId = Guid.NewGuid(),
            ClosedLoopDistanceMetres = 400
        };
        var changed = new CoursePointDistance
        {
            CoursePointStartId = Guid.NewGuid(), CoursePointEndId = Guid.NewGuid(),
            Distance = 999, FacilityId = Guid.NewGuid(), CourseLayoutId = Guid.NewGuid(),
            ClosedLoopDistanceMetres = 500
        };
        Assert.Equal(1, await Produce(timing, simple, distanceOverride: original));
        Assert.Equal(0, await Produce(timing, simple, distanceOverride: changed));
        await using var db = Db();
        var sample = await db.Set<Sample>().SingleAsync();
        Assert.Equal(original.FacilityId, sample.FacilityId);
        Assert.Equal(original.CoursePointStartId, sample.CoursePointStartId);
        Assert.Equal(original.CoursePointEndId, sample.CoursePointEndId);
        Assert.Equal(original.CourseLayoutId, sample.CourseLayoutId);
        Assert.Equal(original.ClosedLoopDistanceMetres, sample.ClosedLoopDistanceMetres);
        Assert.Equal(original.Distance, sample.Distance);
        Assert.Equal(10, sample.Speed);
        Assert.Equal(DateTimeKind.Utc, sample.TimeStart.Kind);
        var receipt = await db.Set<SampleCompleteOutbox>().SingleAsync();
        Assert.Equal(sample.Id, receipt.SampleId);
        Assert.Equal(original.Distance, receipt.Distance);
        Assert.Equal(original.CoursePointStartId, receipt.CoursePointStart);
        Assert.Equal(original.CoursePointEndId, receipt.CoursePointEnd);
        Assert.NotNull(receipt.SmbEnqueuedAtUtc);
        Assert.Equal(1, await Count());
    }

    private static TimingSampleComplete Timing(Guid rider) =>
        new(Start, Start.AddSeconds(10), Guid.NewGuid(), Guid.NewGuid(), rider);

    private static SimpleStatisticsItemConfig ProductionSimple()
    {
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "SMB lap", Distance = 250 };
        return new() { Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid() };
    }

    private async Task<(SimpleStatisticsItemConfig Simple, MultiStatisticsItemConfig Multi)> ProductionDefinitions(
        SimpleStatisticsItemConfig? simple = null, int repetitions = 1)
    {
        simple ??= ProductionSimple();
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "SMB Multi", Distance = 500 };
        var multi = new MultiStatisticsItemConfig { Id = Guid.NewGuid(), StatisticsItem = item,
            StatisticsItemId = item.Id, ParentConfig = simple, ParentConfigId = simple.Id, Repetitions = repetitions };
        await using var db = Db();
        db.AddRange(simple, multi);
        await db.SaveChangesAsync();
        return (simple, multi);
    }

    private async Task<int> Produce(TimingSampleComplete message, SimpleStatisticsItemConfig simple,
        CancellationToken cancellationToken = default, Action<StatisticsDbContext>? configure = null,
        CoursePointDistance? distanceOverride = null)
    {
        var facilities = DispatchProxy.Create<IFacilitiesClient, TimingSampleEventTimeTests.FacilitiesProxy>();
        ((TimingSampleEventTimeTests.FacilitiesProxy)facilities).Resolve = (_, _, _, _, _) =>
            Task.FromResult(distanceOverride ?? new CoursePointDistance { CoursePointStartId = simple.CoursePointStart,
                CoursePointEndId = simple.CoursePointEnd, Distance = 250,
                FacilityId = Guid.Parse("99720000-0000-0000-0000-000000000001") });
        await using var scope = provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        configure?.Invoke(db);
        await new TimingSampleHandler(db, facilities, scope.ServiceProvider.GetRequiredService<Metrics>(),
            NullLogger<TimingSampleHandler>.Instance, scope.ServiceProvider.GetRequiredService<StatisticsHeadRelease>())
            .OnHandle(message, cancellationToken);
        return ((TimingSampleEventTimeTests.FacilitiesProxy)facilities).Calls;
    }

    private Task Consume(ServiceBusMessage wire) => Consume(wire, new ProductionEmptyOwnershipReader());

    private async Task Consume(ServiceBusMessage wire, ITransponderOwnershipReader reader,
        CancellationToken cancellationToken = default, Action<StatisticsDbContext>? configure = null, bool? sample = null)
    {
        await using var scope = provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        configure?.Invoke(db);
        // Local cache/empty authoritative ownership reader keep the test independent
        // of Redis/OIDC HTTP; domain services and PostgreSQL are real.
        await using var local = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var statistics = new StatisticsService(db, local.GetRequiredService<HybridCache>());
        var ownership = new StatisticsOwnershipService(db, reader,
            scope.ServiceProvider.GetRequiredService<StatisticsHeadRelease>());
        var context = WorkerHandlerTests.TestConsumerContext.Create(new Dictionary<string, object>(wire.ApplicationProperties));
        // Select the actual consumer from the serialized body, not the header under test.
        if (sample ?? wire.Body.ToString().Contains("coursePointStart", StringComparison.OrdinalIgnoreCase))
        {
            var message = JsonSerializer.Deserialize<SampleComplete>(wire.Body.ToString(), JsonOptions)!;
            if (message.TransponderId != Guid.Empty) Assert.Equal(message.TransponderId.ToString(), wire.SessionId);
            await new SampleCompleteHandler(statistics, ownership, scope.ServiceProvider.GetRequiredService<Metrics>(),
                NullLogger<SampleCompleteHandler>.Instance) { Context = context }.OnHandle(message, cancellationToken);
        }
        else
        {
            var message = JsonSerializer.Deserialize<EntryCreated>(wire.Body.ToString(), JsonOptions)!;
            if (message.TransponderId != Guid.Empty) Assert.Equal(message.TransponderId.ToString(), wire.SessionId);
            await new EntryCreatedHandler(statistics, ownership, NullLogger<EntryCreatedHandler>.Instance)
                { Context = context }.OnHandle(message, cancellationToken);
        }
    }

    private static ServiceBusMessage CopyWire(ServiceBusMessage source)
    {
        var copy = new ServiceBusMessage(source.Body) { SessionId = source.SessionId };
        foreach (var header in source.ApplicationProperties) copy.ApplicationProperties.Add(header);
        return copy;
    }

    private static ServiceBusMessage SampleWire(SampleCompleteOutbox row)
    {
        var wire = new ServiceBusMessage(BinaryData.FromString(JsonSerializer.Serialize(Message(row), JsonOptions)))
            { SessionId = row.TransponderId.ToString() };
        wire.ApplicationProperties["MessageName"] = nameof(SampleComplete);
        wire.ApplicationProperties[ReceiptHeader] = row.SampleId.ToString();
        return wire;
    }

    private static ServiceBusMessage EntryWire(EntryCreatedOutbox row)
    {
        var wire = new ServiceBusMessage(BinaryData.FromString(JsonSerializer.Serialize(new EntryCreated(
            row.TransponderId, row.TimeStart, row.TimeEnd, row.StatisticsItemId, row.ConfigItemId, row.EntryId), JsonOptions)))
            { SessionId = row.TransponderId.ToString() };
        wire.ApplicationProperties["MessageName"] = nameof(EntryCreated);
        wire.ApplicationProperties[ReceiptHeader] = row.EntryId.ToString();
        if (row.Single) wire.ApplicationProperties["Single"] = true;
        return wire;
    }

    private Task RejectEnqueue(bool enabled) => Sql(connection, enabled
        ? $"ALTER TABLE {Schema}.smb_outbox ADD CONSTRAINT production_reject_insert CHECK (false) NOT VALID"
        : $"ALTER TABLE {Schema}.smb_outbox DROP CONSTRAINT production_reject_insert");

    private sealed class ProductionEmptyOwnershipReader : ITransponderOwnershipReader
    {
        public Task<IReadOnlyList<VeloTime.Module.Timing.Interface.Data.TransponderOwnershipDTO>> ReadAsync(
            Guid transponderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VeloTime.Module.Timing.Interface.Data.TransponderOwnershipDTO>>([]);
    }
}
