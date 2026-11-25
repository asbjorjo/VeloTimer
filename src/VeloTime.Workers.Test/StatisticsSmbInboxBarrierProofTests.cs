using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using Assert = Xunit.Assert;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsSmbOutboxProofTests
{
    [PostgreSqlStatisticsFact]
    public async Task DurableInboxSketchParksEarlyReversedDuplicatesAndFailedProcessingWithoutBrokerRetry()
    {
        // Test-only infrastructure, NOT a proposed migration. The retained local receipt supplies
        // the immutable payload; a production inbox must validate/persist all required wire evidence.
        await Sql(connection, $"CREATE TABLE {Schema}.proof_inbox (receipt_id uuid PRIMARY KEY)");
        var first = Receipt();
        var second = Receipt(first.TransponderId);
        var unrelated = Receipt();
        await Enqueue(first);
        await Enqueue(second);
        await Enqueue(unrelated);

        // Simulate a reversed/early accepted copy: a successful return means durable staging,
        // never that domain processing completed. Missing/conflicting evidence must still fail.
        await StageSample(Message(second), second.SampleId);
        await StageSample(Message(second), second.SampleId);
        Assert.False(await ProcessStagedSample(second.SampleId));
        Assert.Equal(1, await Scalar<int>($"SELECT count(*)::int FROM {Schema}.proof_inbox"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageSample(Message(second) with { Distance = 999 }, second.SampleId));

        sender.OnAccepted = async message =>
        {
            var id = Guid.Parse((string)message.ApplicationProperties[ReceiptHeader]);
            var payload = JsonSerializer.Deserialize<SampleComplete>(message.Body.ToString(), JsonOptions)!;
            await StageSample(payload, id); // commits BEFORE sender returns / SMB delivery mark
        };
        sender.Available = true;
        await Until(async () => await DeliveredCount() == 3);
        await Bus.Stop(); // the durable drain below requires no live broker or receiver
        Assert.Equal(3, sender.Accepted.Count);
        Assert.Equal(3, await Scalar<int>($"SELECT count(*)::int FROM {Schema}.proof_inbox"));

        Assert.False(await ProcessStagedSample(second.SampleId));
        Assert.True(await ProcessStagedSample(unrelated.SampleId)); // blocked rider is not a global stall
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessStagedSample(first.SampleId, fail: true));
        await using (var db = Db())
            Assert.Null((await db.Set<SampleCompleteOutbox>().SingleAsync(r => r.SampleId == first.SampleId)).EntriesCreatedAtUtc);
        Assert.False(await ProcessStagedSample(second.SampleId)); // rollback cannot release barrier
        Assert.True(await ProcessStagedSample(first.SampleId));
        Assert.True(await ProcessStagedSample(second.SampleId));
        Assert.True(await ProcessStagedSample(first.SampleId)); // idempotent completion
        Assert.Equal(3, sender.Accepted.Count); // no broker requeue/retry to wait on processing
        Assert.Equal(3, await DeliveredCount());
    }

    private async Task StageSample(SampleComplete payload, Guid receiptId)
    {
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync();
        var receipt = await db.Set<SampleCompleteOutbox>().SingleAsync(r => r.SampleId == receiptId);
        if (payload != Message(receipt)) throw new InvalidOperationException("Conflicting inbox payload");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO statistics_processor.proof_inbox (receipt_id) VALUES ({receiptId})
            ON CONFLICT (receipt_id) DO NOTHING
            """);
        await tx.CommitAsync();
    }

    private async Task<bool> ProcessStagedSample(Guid receiptId, bool fail = false)
    {
        await using var db = Db(); // every attempt can be a new process/scope
        await using var tx = await db.Database.BeginTransactionAsync();
        var source = await db.Set<SampleCompleteOutbox>().SingleAsync(r => r.SampleId == receiptId);
        var key = $"statistics-transponder:{source.TransponderId:D}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
        await db.Entry(source).ReloadAsync();
        var staged = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM statistics_processor.proof_inbox WHERE receipt_id = {receiptId}
            """).SingleAsync();
        if (staged != 1) return false;
        if (source.EntriesCreatedAtUtc.HasValue) return true;
        var delivered = await db.Database.SqlQuery<bool>($"""
            SELECT delivery_complete AS "Value" FROM statistics_processor.smb_outbox
            WHERE headers ->> 'StatisticsReceiptId' = {receiptId.ToString()}
              AND headers ->> 'MessageName' = 'SampleComplete'
            """).SingleAsync();
        if (!delivered || await db.Set<SampleCompleteOutbox>().AnyAsync(r =>
                r.TransponderId == source.TransponderId && r.Id < source.Id && r.EntriesCreatedAtUtc == null))
            return false; // durably parked, not a broker exception, no busy loop

        // Stand-in for the existing domain transaction: real handler integration (including
        // outputs/decisions and EntryCreated's Single/version rules) remains a separate gate.
        source.EntriesCreatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        if (fail) throw new InvalidOperationException("injected processing failure");
        await tx.CommitAsync();
        return true;
    }
}
