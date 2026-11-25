using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Outbox.PostgreSql.Configuration;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using Assert = Xunit.Assert;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsSmbOutboxProofTests
{
    // Test-only algorithm. No custom dispatcher/inbox: all dispatch goes through stock SMB.
    // Both adapters use real retained domain tables and the production transponder lock key.
    [PostgreSqlStatisticsTheory]
    [InlineData(false, "ack")]
    [InlineData(true, "ack")]
    [InlineData(false, "lost-ack")]
    [InlineData(true, "lost-ack")]
    [InlineData(false, "update-failure")]
    [InlineData(true, "update-failure")]
    public async Task HeadGateSuccessorCannotOvertakeInFlightPredecessor(bool entry, string failure)
    {
        await CreateHeadProbe();
        var first = HeadReceipt.New(entry);
        var second = HeadReceipt.New(entry, first.Rider);
        var third = HeadReceipt.New(entry, first.Rider, single: false);
        await InsertHeadReceipt(first);
        await InsertHeadReceipt(second);
        await InsertHeadReceipt(third);
        Assert.Equal(1, await Count()); // all receipts persisted; only the head in SMB
        Assert.Equal(3, (await HeadRows(null, entry, first.Rider)).Count);

        if (failure == "update-failure")
            await Sql(connection, $"""
                CREATE FUNCTION {Schema}.reject_delivery_mark() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.delivery_complete THEN RAISE EXCEPTION 'injected delivery mark failure'; END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER reject_delivery_mark BEFORE UPDATE ON {Schema}.smb_outbox
                FOR EACH ROW EXECUTE FUNCTION {Schema}.reject_delivery_mark();
                """);

        var successorCommitted = Signal();
        var releaseSender = Signal();
        var firstAcceptance = 0;
        var secondWasSentBeforeMark = false;
        sender.FailAfterAccept = failure == "lost-ack";
        sender.OnAccepted = async wire =>
        {
            var id = WireId(wire);
            if (id == first.Key && Interlocked.Increment(ref firstAcceptance) == 1)
            {
                try
                {
                    // This is a separate consumer transaction, not the sender's connection.
                    Assert.False(await DeliveryStatus(first.Key, first.Name));
                    await ProcessHeadReceipt(first, wire);
                    Assert.Equal(2, await Count());
                    Assert.False(await DeliveryStatus(first.Key, first.Name));
                    Assert.False(await DeliveryStatus(second.Key, second.Name));
                    successorCommitted.SetResult();
                    await releaseSender.Task.WaitAsync(TimeSpan.FromSeconds(20));
                }
                catch (Exception ex) { successorCommitted.TrySetException(ex); throw; }
            }
            else if (id == second.Key)
            {
                secondWasSentBeforeMark |= !await DeliveryStatus(first.Key, first.Name);
                await ProcessHeadReceipt(second, wire);
            }
            else if (id == third.Key)
            {
                Assert.True(await DeliveryStatus(second.Key, second.Name));
                await ProcessHeadReceipt(third, wire);
            }
            else
            {
                // Retried/delayed predecessor: validate it and do no effects/enqueue twice.
                await ProcessHeadReceipt(first, wire);
            }
        };
        sender.Available = true;
        try
        {
            await successorCommitted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(180); // several idle poll intervals while sender is held
            Assert.Equal(new[] { first.Key }, sender.Accepted.Select(WireId).ToArray());
            Assert.Equal(1, await EffectCount());
        }
        finally { releaseSender.TrySetResult(); }

        if (failure != "ack")
        {
            if (failure == "lost-ack")
                await Until(async () => await Scalar<int>($"""
                    SELECT delivery_attempt FROM {Schema}.smb_outbox
                    WHERE headers ->> '{ReceiptHeader}' = '{first.Key}'
                    """) > 0);
            else
                await Until(() => Task.FromResult(sender.Accepted.Count >= 2));
            await Bus.Stop();
            Assert.False(await DeliveryStatus(first.Key, first.Name));
            Assert.False(await DeliveryStatus(second.Key, second.Name));
            Assert.All(sender.Accepted, m => Assert.Equal(first.Key, WireId(m)));
            Assert.Equal(1, await EffectCount());
            Assert.Equal(2, await Count());
            if (failure == "update-failure")
                await Sql(connection, $"DROP TRIGGER reject_delivery_mark ON {Schema}.smb_outbox");
            sender.FailAfterAccept = false;
            var oldProvider = provider!;
            var oldInstance = oldProvider.GetRequiredService<IInstanceIdProvider>().GetInstanceId();
            await oldProvider.DisposeAsync();
            master = null;
            provider = null;
            await StartProofProvider(); // new repository, poller, connection and lock identity
            Assert.NotEqual(oldInstance, provider!.GetRequiredService<IInstanceIdProvider>().GetInstanceId());
        }

        await Until(async () => await DeliveredCount() == 3);
        Assert.False(secondWasSentBeforeMark);
        var accepted = sender.Accepted.Select(WireId).ToArray();
        var successorIndex = Array.IndexOf(accepted, second.Key);
        Assert.True(successorIndex > 0);
        Assert.All(accepted.Take(successorIndex), id => Assert.Equal(first.Key, id));
        Assert.Equal(new[] { second.Key, third.Key }, accepted.Skip(successorIndex).ToArray());
        Assert.Equal(3, await EffectCount());
        Assert.Equal(3, await Count());

        // A duplicate arriving AFTER the whole stream completed is still harmless.
        await ProcessHeadReceipt(first, sender.Accepted.First());
        Assert.Equal(3, await EffectCount());
        Assert.Equal(3, await Count());
        var malformed = new ServiceBusMessage(BinaryData.FromString("{}"))
        {
            SessionId = sender.Accepted.First().SessionId
        };
        foreach (var header in sender.Accepted.First().ApplicationProperties)
            malformed.ApplicationProperties.Add(header);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessHeadReceipt(first, malformed));
    }

    [PostgreSqlStatisticsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeadGateProcessingRollbackCannotReleaseSuccessorAndDeadLetterIsFailStop(bool entry)
    {
        await CreateHeadProbe();
        var first = HeadReceipt.New(entry);
        var second = HeadReceipt.New(entry, first.Rider);
        await InsertHeadReceipt(first);
        await InsertHeadReceipt(second);
        sender.Available = true;
        await Until(() => DeliveryStatus(first.Key, first.Name));
        var wire = sender.Accepted.Single();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessHeadReceipt(first, wire, fail: true));
        Assert.Equal(0, await EffectCount());
        Assert.Equal(1, await Count()); // successor SMB insert rolled back with domain effects
        Assert.Null((await HeadRows(null, entry, first.Rider)).First().Completed);

        // A broker-dead-lettered/unconsumed head is delivered but NOT domain-complete.
        // No polling worker is needed for recovery: explicit redrive of that stable receipt.
        var unrelated = HeadReceipt.New(entry);
        await InsertHeadReceipt(unrelated);
        await Until(() => DeliveryStatus(unrelated.Key, unrelated.Name));
        Assert.DoesNotContain(sender.Accepted, m => WireId(m) == second.Key);
        Assert.Equal(0, await EffectCount());
        await ProcessHeadReceipt(first, wire);
        await Until(() => DeliveryStatus(second.Key, second.Name));
        await ProcessHeadReceipt(second, sender.Accepted.Single(m => WireId(m) == second.Key));
        Assert.Equal(2, await EffectCount());
        Assert.Equal(3, await Count());
    }

    [PostgreSqlStatisticsTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HeadGateInsertAndEmptyStreamCompletionSerializeInBothDirections(bool entry, bool insertFirst)
    {
        await CreateHeadProbe();
        var first = HeadReceipt.New(entry);
        var second = HeadReceipt.New(entry, first.Rider);
        await InsertHeadReceipt(first);
        sender.Available = true;
        await Until(() => DeliveryStatus(first.Key, first.Name));
        await Bus.Stop();
        var holdingLock = Signal();
        var releaseLock = Signal();
        var contenderPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Hold()
        {
            holdingLock.SetResult();
            await releaseLock.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        var winner = insertFirst
            ? InsertHeadReceipt(second, Hold)
            : ProcessHeadReceipt(first, sender.Accepted.Single(), afterLock: Hold);
        await holdingLock.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var contender = insertFirst
            ? ProcessHeadReceipt(first, sender.Accepted.Single(), beforeLock: pid => contenderPid.SetResult(pid))
            : InsertHeadReceipt(second, beforeLock: pid => contenderPid.SetResult(pid));
        try
        {
            var pid = await contenderPid.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Prove actual lock contention, not just two tasks scheduled serially.
            await Until(async () => await Scalar<bool>($"""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                               WHERE pid = {pid} AND wait_event = 'advisory')
                """));
            Assert.False(contender.IsCompleted);
        }
        finally { releaseLock.TrySetResult(); }
        await Task.WhenAll(winner, contender);
        Assert.Equal(2, await Count()); // no missed wakeup and no double enqueue
        Assert.Equal(1, await EffectCount());
        await Bus.Start();
        await Until(() => DeliveryStatus(second.Key, second.Name));
        Assert.Equal(new[] { first.Key, second.Key }, sender.Accepted.Select(WireId).ToArray());
        await ProcessHeadReceipt(second, sender.Accepted.Last());
        Assert.Equal(2, await EffectCount());
    }

    [PostgreSqlStatisticsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeadGateAbortedTransportRowRemainsVisibleAndNeedsExplicitRepair(bool entry)
    {
        await CreateHeadProbe();
        var first = HeadReceipt.New(entry);
        var second = HeadReceipt.New(entry, first.Rider);
        await InsertHeadReceipt(first);
        await InsertHeadReceipt(second);
        provider!.GetRequiredService<PostgreSqlOutboxSettings>().MaxDeliveryAttempts = 1;
        // Available=false fails before transport acceptance.
        await Until(async () => await Scalar<bool>($"SELECT delivery_aborted FROM {Schema}.smb_outbox"));
        Assert.Empty(sender.Accepted);
        Assert.False(await DeliveryStatus(first.Key, first.Name));
        Assert.Equal(1, await Count());
        Assert.Equal(0, await EffectCount());
        sender.Available = true;
        var unrelated = HeadReceipt.New(entry);
        await InsertHeadReceipt(unrelated);
        await Until(() => DeliveryStatus(unrelated.Key, unrelated.Name));
        Assert.DoesNotContain(sender.Accepted, m => WireId(m) == first.Key || WireId(m) == second.Key);
        await Bus.Stop();
        // Test-only operator repair AFTER fixing the sender; never mark domain processing done.
        await Sql(connection, $"""
            UPDATE {Schema}.smb_outbox SET delivery_aborted=false, delivery_attempt=0,
                lock_expires_on=now() - interval '1 minute'
            WHERE NOT delivery_complete
            """);
        await Bus.Start();
        await Until(() => DeliveryStatus(first.Key, first.Name));
        await ProcessHeadReceipt(first, sender.Accepted.Single(m => WireId(m) == first.Key));
        await Until(() => DeliveryStatus(second.Key, second.Name));
        Assert.Equal(3, await Count());
        Assert.Equal(1, await EffectCount());
    }

    [PostgreSqlStatisticsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeadGateProcessedButAckLostAndAbortedDoesNotStrandSuccessor(bool entry)
    {
        await CreateHeadProbe();
        var first = HeadReceipt.New(entry);
        var second = HeadReceipt.New(entry, first.Rider);
        await InsertHeadReceipt(first);
        await InsertHeadReceipt(second);
        provider!.GetRequiredService<PostgreSqlOutboxSettings>().MaxDeliveryAttempts = 1;
        sender.FailAfterAccept = true;
        sender.OnAccepted = async wire =>
        {
            if (WireId(wire) == second.Key)
            {
                Assert.False(await DeliveryStatus(first.Key, first.Name));
                Assert.True(await Scalar<bool>($"""
                    SELECT delivery_aborted FROM {Schema}.smb_outbox
                    WHERE headers ->> '{ReceiptHeader}' = '{first.Key}'
                    """));
                sender.FailAfterAccept = false;
                await ProcessHeadReceipt(second, wire);
            }
            else await ProcessHeadReceipt(first, wire);
        };
        sender.Available = true;
        await Until(async () => await Count() == 2); // successor does not exist until P processes
        await Until(() => DeliveryStatus(second.Key, second.Name));
        var accepted = sender.Accepted.Select(WireId).ToArray();
        Assert.All(accepted.Take(accepted.Length - 1), id => Assert.Equal(first.Key, id));
        Assert.Equal(second.Key, accepted.Last());
        Assert.Equal(2, await EffectCount());
        Assert.Equal(2, await Count());
        Assert.Equal(1, await DeliveredCount()); // no fabricated predecessor delivery mark
        await Bus.Stop();
        await Sql(connection, $"""
            UPDATE {Schema}.smb_outbox SET delivery_aborted=false, delivery_attempt=0,
                lock_expires_on=now() - interval '1 minute'
            WHERE NOT delivery_complete
            """);
        await Bus.Start();
        await Until(() => DeliveryStatus(first.Key, first.Name));
        Assert.Equal(first.Key, WireId(sender.Accepted.Last())); // genuine delayed duplicate
        Assert.Equal(2, await EffectCount());
        Assert.Equal(2, await Count());
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Guid WireId(ServiceBusMessage wire) => Guid.Parse((string)wire.ApplicationProperties[ReceiptHeader]);
    private Task CreateHeadProbe() => Sql(connection, $"""
        CREATE TABLE {Schema}.proof_effect (receipt_id uuid PRIMARY KEY, completed_at timestamptz NOT NULL)
        """);
    private Task<int> EffectCount() => Scalar<int>($"SELECT count(*)::int FROM {Schema}.proof_effect");

    private async Task InsertHeadReceipt(HeadReceipt row, Func<Task>? afterLock = null, Action<int>? beforeLock = null)
    {
        await using var scope = provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await HeadLock(db, row.Rider, beforeLock);
        if (afterLock is not null) await afterLock();
        db.Add(row.Entity);
        await db.SaveChangesAsync();
        await ReleaseHead(db, scope.ServiceProvider, row.IsEntry, row.Rider);
        await tx.CommitAsync();
    }

    private async Task ProcessHeadReceipt(HeadReceipt expected, ServiceBusMessage wire, bool fail = false,
        Func<Task>? afterLock = null, Action<int>? beforeLock = null)
    {
        await using var scope = provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await HeadLock(db, expected.Rider, beforeLock);
        if (afterLock is not null) await afterLock();
        var rows = await HeadRows(db, expected.IsEntry, expected.Rider);
        var source = rows.Single(r => r.Key == WireId(wire)); // missing/ambiguous never acknowledged
        var payload = expected.IsEntry
            ? (object?)JsonSerializer.Deserialize<EntryCreated>(wire.Body.ToString(), JsonOptions)
            : JsonSerializer.Deserialize<SampleComplete>(wire.Body.ToString(), JsonOptions);
        if (!Equals(source.Payload, payload) ||
            (string)wire.ApplicationProperties["MessageName"] != source.Name ||
            wire.SessionId != source.Rider.ToString() ||
            wire.ApplicationProperties.ContainsKey("Single") != (source.Entry?.Single ?? false) ||
            (source.Entry?.Single == true && !Equals(true, wire.ApplicationProperties["Single"])))
            throw new InvalidOperationException("Conflicting completion payload/headers");
        if (source.Completed.HasValue) return; // validate even delayed duplicates
        if (rows.First(r => !r.Completed.HasValue).Key != source.Key)
            throw new InvalidOperationException("Unprocessed predecessor");
        source.Complete();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO statistics_processor.proof_effect VALUES ({source.Key}, {DateTime.UtcNow})
            """);
        await db.SaveChangesAsync();
        await ReleaseHead(db, scope.ServiceProvider, source.IsEntry, source.Rider);
        if (fail) throw new InvalidOperationException("injected domain transaction rollback");
        await tx.CommitAsync();
    }

    private static async Task HeadLock(StatisticsDbContext db, Guid rider, Action<int>? beforeLock)
    {
        if (beforeLock is not null)
            beforeLock(await db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync());
        var key = $"statistics-transponder:{rider:D}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
    }

    private async Task ReleaseHead(StatisticsDbContext db, IServiceProvider services, bool entry, Guid rider)
    {
        var head = (await HeadRows(db, entry, rider)).FirstOrDefault(r => !r.Completed.HasValue);
        if (head is null) return;
        var mappings = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM statistics_processor.smb_outbox
            WHERE headers ->> 'StatisticsReceiptId' = {head.Key.ToString()}
              AND headers ->> 'MessageName' = {head.Name}
            """).SingleAsync();
        if (mappings > 1) throw new InvalidOperationException("Ambiguous retained SMB receipt");
        if (mappings == 1) return; // includes pending, delivered and aborted: never silently replace
        if (head.Sample is not null) await Publish(services, Message(head.Sample), head.Key);
        else await Publish(services, (EntryCreated)head.Payload, head.Key, head.Entry!.Single);
    }

    private async Task<List<HeadReceipt>> HeadRows(StatisticsDbContext? dbForRead, bool entry, Guid rider)
    {
        if (dbForRead is null)
        {
            await using var db = Db();
            return await HeadRows(db, entry, rider);
        }
        if (entry)
            return (await dbForRead.Set<EntryCreatedOutbox>().Where(r => r.TransponderId == rider)
                .OrderBy(r => r.Id).ToListAsync()).Select(r => new HeadReceipt(null, r)).ToList();
        return (await dbForRead.Set<SampleCompleteOutbox>().Where(r => r.TransponderId == rider)
            .OrderBy(r => r.Id).ToListAsync()).Select(r => new HeadReceipt(r, null)).ToList();
    }

    private sealed record HeadReceipt(SampleCompleteOutbox? Sample, EntryCreatedOutbox? Entry)
    {
        public bool IsEntry => Entry is not null;
        public object Entity => (object?)Sample ?? Entry!;
        public Guid Key => Sample?.SampleId ?? Entry!.EntryId;
        public Guid Rider => Sample?.TransponderId ?? Entry!.TransponderId;
        public string Name => IsEntry ? nameof(EntryCreated) : nameof(SampleComplete);
        public DateTime? Completed => IsEntry ? Entry!.MultiDecisionsCreatedAtUtc : Sample!.EntriesCreatedAtUtc;
        public object Payload => Sample is not null ? Message(Sample) : new EntryCreated(
            Entry!.TransponderId, Entry.TimeStart, Entry.TimeEnd, Entry.StatisticsItemId, Entry.ConfigItemId, Entry.EntryId);
        public void Complete()
        {
            if (Sample is not null) Sample.EntriesCreatedAtUtc = DateTime.UtcNow;
            else Entry!.MultiDecisionsCreatedAtUtc = DateTime.UtcNow;
        }
        public static HeadReceipt New(bool entry, Guid? rider = null, bool single = true) => entry
            ? new(null, new EntryCreatedOutbox
            {
                EntryId = Guid.NewGuid(), TransponderId = rider ?? Guid.NewGuid(),
                TimeStart = Start, TimeEnd = Start.AddSeconds(1),
                StatisticsItemId = Guid.NewGuid(), ConfigItemId = Guid.NewGuid(), Single = single,
                MultiDecisionVersion = 1
            })
            : new(Receipt(rider), null);
    }
}
