# Durable EntryCreated publication

The processor alone writes and dispatches `statistics.entry_created_outbox`.
It does not register SMB outbox interceptors or read/write the API-owned
`statistics.smb_outbox`; threshold publication and its dispatcher are unchanged.
No public contract or synchronous API changed. StatisticsService's replay identity
and geometry/required-point rules are unchanged.

Both entry-producing handlers request enqueue from StatisticsOwnershipService.
Under its existing transponder advisory lock and StatisticsDbContext transaction,
creation, validity scheduling, ownership/profile assignment and exact outgoing
payloads commit together. One retained row per deterministic EntryId is enforced by
a unique index; retries validate the payload and reuse the receipt, even after
delivery. Do not delete receipts when entries/configs are deleted. There are deliberately
no FKs to mutable/deletable domain data. Zero outputs create no entry receipt.
Later definition additions can still yield new outputs on completion replay.

Rows store every EntryCreated field plus the original `Single=true` header for
SampleComplete-derived events (absent for EntryCreated-derived events). Individual
messages, not a collection envelope, go through the unchanged SMB producer:
`velotime-statistics`, `MessageName=EntryCreated`, transponder GUID SessionId.
Each worker iteration uses a fresh DI scope/context. A PostgreSQL row lock is held
across broker acknowledgement and delivery-mark commit. Exceptions/cancellation
never mark failed work successful. Failed/uncertain acknowledgements, mark failure
or process death may resend the same payload: delivery is **at least once**.
There is no direct-send fallback and no dependence on broker duplicate detection.

Entry sequence IDs are allocated while holding the existing per-transponder
creation lock. The dispatcher selects the oldest pending row per transponder with
`FOR UPDATE SKIP LOCKED`; other replicas cannot overtake a locked predecessor but
can send unrelated riders. An uncommitted creation cannot be dispatched.

## Coordination with SampleComplete

The SampleComplete outbox now retains `entries_created_at_utc`, set by
SampleCompleteHandler in the entry/ownership/outbox transaction, including empty
results. The sample dispatcher does not release a later visible sample for a rider
until its predecessor is both acknowledged and entry creation is committed. A
failed creation therefore blocks later samples rather than allowing a later sample's
downstream entry events to overtake it. Delivery and creation marks may arrive in
either order. Externally republished completion messages without a local outbox row
remain supported; they cannot establish ordering against local source rows.

This is not a global total order: sample source sequence allocation can precede
commit, independent producers/redrives can reorder, and parent EntryCreated
processing is asynchronous. All derived entry payloads are persisted before they
can be sent, and Simple/Multi consumers must remain replay-safe.

## Rollout, historical gate and recovery

1. Stop **all** old Statistics processors; do not run old direct-publication writers
   alongside these writers. Back up and inventory samples, entries, both isolated
   outboxes, queues and dead letters. Follow the authoritative reconciliation or
   bounded approved preproduction reset in `SampleIdentityPersistence.md` and the
   random-ID entry activation gate in `EntryReplay.md`. This migration deletes nothing.
2. Apply `20261123145848_statistics_entry_created_outbox` before the new processor.
   Grant its role DML/sequence rights on the new table and access to the new sample
   column. Index replacement is transactional/nonconcurrent: allow a maintenance
   window for a large retained sample outbox. API/threshold outbox needs no change.
3. Before reset activation, every retained receipt in either isolated outbox blocks
   migration, whether delivered or pending. Existing SampleComplete rows get NULL
   creation marks; their processing state is unknown. Follow the individual
   reconciliation and explicit removal prerequisites in `SampleIdentityPersistence.md`.
   Never infer delivery or manually mark work complete. Stop if unresolved.
4. Events previously sent directly have no historical EntryId publication receipts.
   Authoritative replay may resend them; consumers must tolerate duplicates. No
   automatic backfill guesses whether an old event was sent. Data reset does not
   regenerate deleted results; regeneration requires retained Timing evidence,
   configurations and a coordinated replay. Never clear the shared SMB outbox.
5. Start upgraded consumers/workers together. Monitor pending EntryCreated count,
   oldest pending age, delivered-but-unprocessed SampleComplete rows, retries/error
   logs, dead letters, DB locks/long transactions, and retained-table growth.
   Poison work intentionally blocks that rider; a single worker can also delay
   unrelated riders while sending/retrying. More replicas improve unrelated progress.
   Correct the cause and retry; no automatic deletion/retention policy is introduced.

Rollback to direct publication loses atomicity and cannot undo already delivered
events. Stop dispatchers and reconcile/back up first. Down refuses while **any**
EntryCreated receipts remain, including delivered rows: deleting these loses
deduplication and pending payloads. Removing the sample creation column also loses
the processing barrier. Retain backups and prefer roll-forward. No reset, historical
replay, operator mark adjustment or production migration was executed by this change.
