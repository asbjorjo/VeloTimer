# Retained SampleComplete receipts

The production protocol and mandatory maintenance cutover are in
[StatisticsProcessorSmbHead.md](StatisticsProcessorSmbHead.md).

`statistics.sample_complete_outbox` is retained domain evidence, **not** a
transport queue. TimingSampleHandler takes `statistics-transponder:{guid}`,
reuses/validates an existing source or persists the enriched Sample and immutable
receipt, saves, and calls StatisticsHeadRelease before committing. Replays never
re-enrich from Facilities or allocate a second local position. UTC instants are
normalized to PostgreSQL precision; quantities remain SI.

Head release publishes only the lowest-ID unprocessed receipt of this rider to
stock SMB in `statistics_processor.smb_outbox`. The concrete SampleComplete,
`StatisticsReceiptId` (SampleId), and first `SmbEnqueuedAtUtc` commit in the same
scoped StatisticsDbContext transaction. The producer preserves MessageName,
topic `velotime-statistics` and the transponder GUID SessionId. There is no direct
send fallback, custom dispatcher, notification gap or startup recovery sweeper.

SampleCompleteHandler validates identity, full immutable payload and headers
under that same lock. Unenqueued, conflicting, missing, ambiguous or reversed
pending messages fail. Completed duplicates validate but do not consult live
definitions/owners or repeat effects/release. Legacy aliases require authoritative
unique-source evidence; they cannot omit identity for newly enqueued work.

All entries, ownership/profile changes, validity scheduling, derived EntryCreated
receipts, and `EntriesCreatedAtUtc` are saved before releasing both independent
stream heads and committing. **Zero outputs are final completion too.** Later
definition changes cannot reopen a completed source. A failed enqueue/commit or
cancellation rolls back the completion, effects and successor publication together.

Receipt IDs express committed local creation order under the rider lock, not event
time. A poison/dead-lettered/unprocessed head blocks this rider's Sample stream;
the Entry stream has its own barrier. At-least-once transport can deliver late
duplicates. `DeliveredAtUtc` remains historical direct-dispatch evidence only;
SMB owns transport acknowledgement/retry state. Retain receipts indefinitely,
even after domain or completed SMB cleanup. Never infer completion from delivery.

Do not fabricate missing receipts or infer source order from timestamps. Stop and
reconcile old processors, queues, in-flight copies, retries and dead letters under
the prerequisite migration's maintenance procedure. Ordinary old installations
may not satisfy it. The API's independent `statistics.smb_outbox` is unchanged
and must not be cleared or adopted by this processor.
