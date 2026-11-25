using Microsoft.EntityFrameworkCore;
using SlimMessageBus.Host;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

public static class StatisticsProcessorStartup
{
    public static async Task StartStatisticsProcessorAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await ValidateAsync(scope.ServiceProvider.GetRequiredService<StatisticsDbContext>(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await host.Services.GetRequiredService<IMasterMessageBus>().Start();
    }

    public static async Task ValidateAsync(StatisticsDbContext storage, CancellationToken cancellationToken)
    {
        const string prerequisite = "20261125075737_statistics_processor_smb_head";
        if (!(await storage.Database.GetAppliedMigrationsAsync(cancellationToken)).Contains(prerequisite))
            throw new InvalidOperationException("Statistics SMB head schema prerequisite is missing; complete maintenance migration first.");
        // No recovery sweeper: a committed pending stream must already have exactly
        // its oldest receipt marked. Missing state is an incident, not permission to send.
        if (await storage.Set<SampleCompleteOutbox>().AnyAsync(r =>
                (r.EntriesCreatedAtUtc != null && r.SmbEnqueuedAtUtc == null && r.DeliveredAtUtc == null) ||
                (r.EntriesCreatedAtUtc == null && (r.DeliveredAtUtc != null ||
                    (r.SmbEnqueuedAtUtc == null && !storage.Set<SampleCompleteOutbox>().Any(earlier =>
                        earlier.TransponderId == r.TransponderId && earlier.Id < r.Id && earlier.EntriesCreatedAtUtc == null)))),
                cancellationToken) ||
            await storage.Set<EntryCreatedOutbox>().AnyAsync(r => r.MultiDecisionVersion != 1 ||
                (r.MultiDecisionsCreatedAtUtc != null && r.SmbEnqueuedAtUtc == null && r.DeliveredAtUtc == null) ||
                (r.MultiDecisionsCreatedAtUtc == null && (r.DeliveredAtUtc != null ||
                    (r.SmbEnqueuedAtUtc == null && !storage.Set<EntryCreatedOutbox>().Any(earlier =>
                        earlier.TransponderId == r.TransponderId && earlier.Id < r.Id && earlier.MultiDecisionsCreatedAtUtc == null)))),
                cancellationToken))
            throw new InvalidOperationException("Inconsistent/legacy Statistics head state; stop writers and reconcile retained receipts.");

        if (await storage.Set<SampleCompleteOutbox>().AnyAsync(r => r.SmbEnqueuedAtUtc != null && r.EntriesCreatedAtUtc == null, cancellationToken) ||
            await storage.Set<EntryCreatedOutbox>().AnyAsync(r => r.SmbEnqueuedAtUtc != null && r.MultiDecisionsCreatedAtUtc == null, cancellationToken))
        {
            // Completed SMB rows may eventually be cleaned up, pending associations
            // may not. Never recreate a lost/aborted transport row automatically.
            var invalid = await storage.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM (
                    SELECT r.receipt_id, r.message_name FROM (
                        SELECT sample_id AS receipt_id, 'SampleComplete' AS message_name
                        FROM statistics.sample_complete_outbox
                        WHERE smb_enqueued_at_utc IS NOT NULL AND entries_created_at_utc IS NULL
                        UNION ALL
                        SELECT entry_id, 'EntryCreated' FROM statistics.entry_created_outbox
                        WHERE smb_enqueued_at_utc IS NOT NULL AND multi_decisions_created_at_utc IS NULL
                    ) r
                    LEFT JOIN statistics_processor.smb_outbox o
                        ON o.headers ->> 'StatisticsReceiptId' = r.receipt_id::text
                        AND o.headers ->> 'MessageName' = r.message_name
                    GROUP BY r.receipt_id, r.message_name
                    HAVING count(o.headers) <> 1
                ) invalid
                """).SingleAsync(cancellationToken);
            if (invalid != 0)
                throw new InvalidOperationException("Pending Statistics receipt has a missing/ambiguous SMB association; operator recovery is required.");
        }
    }
}
