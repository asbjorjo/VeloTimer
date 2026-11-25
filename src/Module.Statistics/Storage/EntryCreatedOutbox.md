# Retained EntryCreated receipts

See [StatisticsProcessorSmbHead.md](StatisticsProcessorSmbHead.md) for the
production algorithm, schema prerequisite, retention and operator recovery.

`statistics.entry_created_outbox` retains immutable domain publication receipts
independently of entries/configs. The unique EntryId and local bigint Id preserve
replay identity and rider creation order. Retain payloads, `Single`, version,
enqueue/completion markers and the Multi decision ledger indefinitely.

Under StatisticsOwnershipService's transponder advisory transaction lock, all
domain creation, ownership, scheduling and receipts are saved before the central
StatisticsHeadRelease helper selects the oldest unprocessed Entry receipt.
StatisticsService may create derived receipts internally; release therefore runs
after the **whole** operation, not just handler-visible returned entries.

Only that head is individually published to the scoped stock SMB outbox in
`statistics_processor`, with `StatisticsReceiptId=EntryId`, MessageName,
transponder SessionId and the original Single header (true for Simple; **absent**
for Multi). Its first `SmbEnqueuedAtUtc` and SMB insertion commit atomically.
No custom dispatcher/worker remains. The API continues using its own independent
`statistics.smb_outbox` and dispatcher.

EntryCreatedHandler validates the retained immutable receipt and headers before
idempotency, requires version 1 and rejects an unprocessed predecessor regardless
of Single. Pending Singles run the original Multi selection/decision ledger;
no-result and empty eligible sets are final decisions. Pending non-Singles
validate their Multi configuration, do no recursion, and **also** commit
`MultiDecisionsCreatedAtUtc`. After every receipt/effect is saved, completion and
successor enqueue commit in the same transaction. The partial unique index allows
only one enqueued-unprocessed head per rider in this stream.

Completed broker duplicates do no effects or release and do not depend on deleted
domain objects or today's definitions. Explicit domain replay continues to check
retained decision/output integrity rather than recreating deleted results.
Malformed late duplicates still fail. New enqueued messages require stable IDs;
unprovable legacy aliases, version-0 sources and missing/conflicting receipts
require maintenance reconciliation, never live synthesis or a successful ack.

The Sample and Entry streams share the rider lock but have separate barriers.
Do not infer processing from SMB acknowledgement or legacy DeliveredAtUtc.
An unprocessed poison/dead-letter head intentionally blocks its stream; unrelated
domain transactions can progress, but the single transport poller does **not**
promise fairness during indefinite transport failure. No exactly-once transport,
global cross-stream order, overlapping deployment or multi-poller guarantee is made.
