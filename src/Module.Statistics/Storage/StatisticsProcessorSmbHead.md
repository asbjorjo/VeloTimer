# Statistics processor stock SMB head gate

The processor now implements this protocol using StatisticsHeadRelease and stock
SMB; the custom dispatchers/workers have been removed. The schema prerequisite is
`20261125075737_statistics_processor_smb_head`, unchanged by the conversion.
The maintenance cutover below is still mandatory, not an automatic upgrade.
Production integration evidence and limits are in
`../../VeloTime.Workers.Test/StatisticsSmbProduction.md`; the original runnable
experiment is in `../../VeloTime.Workers.Test/StatisticsSmbHeadGateProof.md`.

## Retained data contract

The existing retained `SampleCompleteOutbox` and `EntryCreatedOutbox` rows are
domain receipts, not SMB rows. Keep their source-unique indexes, immutable payloads,
`Id` sequence/order, `Single`, version and decision ledger indefinitely, including
after SMB cleanup and deletion of domain objects. `Id` is local creation order
under the shared transponder lock, not event time or a global sequence.

Both entities now have `DateTime? SmbEnqueuedAtUtc`, PostgreSQL
`smb_enqueued_at_utc timestamptz NULL`, with no default/backfill. Set it to UTC
only in the same scoped database transaction as the first successful SMB `Publish`
insert. Never clear or overwrite it, including on delivery failure, retry, abort
or SMB retention cleanup. It is the durable association that this exact receipt
was enqueued once, not the SMB row ID, a broker acknowledgement, or processing
success. Stable source ID and exact payload/header validation bind the message
to the retained receipt. No automatic re-enqueue merely because an SMB row has
been cleaned up or aborted. A missing SMB row for an unprocessed marked receipt
is a recovery incident, not permission to republish.

For newly created protocol receipts:

| `SmbEnqueuedAtUtc` | completion | Meaning |
|---|---|---|
| NULL | NULL | persisted, pending and unprocessed; eligible only if oldest |
| non-NULL | NULL | active head, enqueued but unprocessed (regardless of delivery) |
| non-NULL | non-NULL | processing committed; duplicates validate and are no-ops |
| NULL | non-NULL | **legacy completed receipt only**, not a new successful state |

Completion is `EntriesCreatedAtUtc` for samples and
`MultiDecisionsCreatedAtUtc` for entries. Entry `MultiDecisionVersion` must be 1;
preserve all existing no-result, zero-config, source-order and replay decisions.
Every EntryCreated receipt, including `Single=false`, requires a completion mark:
for a validated non-Single event, commit the explicit no-recursion/no-output
completion and release the next head. Do not infer completion from the absent
`Single` header, delivered time, or broker acknowledgement. The topology must
deliver both forms to this processor. The partial `(transponder_id,id)` indexes
select unprocessed heads; separate partial unique indexes permit at most one
enqueued *unprocessed* receipt per transponder per stream. They do not prove
the marked row is oldest, nor that an SMB insert occurred. Those are transactional
application invariants. Legacy `DeliveredAtUtc` is retained as historical direct
dispatch evidence only and must not be used as the new completion or enqueue mark.

All receipt writers and consumers take the same PostgreSQL transaction advisory
lock `statistics-transponder:{guid}` through commit, including replays and derived
receipt insertions. There is one head independently in each stream:

1. Insert/reuse validated immutable receipt(s), allocate `Id` while locked, save.
   Find the lowest-ID unprocessed receipt for that stream/rider. If its marker is
   NULL, publish only that concrete message and persist its marker in the same
   transaction. Otherwise do nothing; never skip or re-enqueue that head.
2. Consumer validates source identity, type, exact payload and headers, including
   `Single`, against one retained receipt. Completed duplicates do no effects or
   release. Reject missing/conflicting receipts and out-of-order unprocessed work.
   Atomically perform original effects and all frozen Multi decisions, including
   empty sets, save completion, then enqueue/mark the next head before commit.
   Save completion before marking the successor so the unique partial index
   permits it. Sample processing also inserts derived Entry receipts and activates
   their head in that transaction; entry processing does likewise for its outputs.
3. If insertion races completion, the same lock ensures either completion sees
   the insertion or insertion sees the completed stream. Rollback must undo
   receipt, marker, SMB row, decisions, effects and successor release together.

The database cannot enforce immutable marker/payload against arbitrary SQL, oldest
selection, shared transaction, or broker state. Restrict write privileges and test
all writers; unique indexes are a final defense, not a substitute for the lock.
Stock SMB retries are at-least-once; delayed duplicates can arrive after successors.
An unprocessed poison or dead-letter head blocks its stream. Monitor pending,
enqueued-unprocessed age, SMB aborted rows, retries and dead letters; repair and
explicitly redrive the same identity, never skip, delete or fake success.

## Schema and maintenance cutover

`20261125075737_statistics_processor_smb_head` creates empty PostgreSQL schema
`statistics_processor` in the same transaction as the columns/indexes. Configure
the **processor** stock SMB PostgreSQL outbox to use exactly that schema. SMB
runtime owns and initializes its own tables/functions there; EF intentionally
does not create or model them. Use the proof's single poller, `MaintainSequence`
and batch size one; do not infer exactly-once delivery or broker settlement from
that configuration. Grant the migration role CREATE SCHEMA and DDL,
and grant the processor role the schema and runtime DDL/DML/sequence privileges
SMB requires. Do not point it at or modify API `statistics.smb_outbox`; the API
keeps its existing independent outbox and history. EF history remains
`ef.__ef_migrations_history`.

Stop the single old processor, all receipt writers/dispatchers and consumers;
prevent restart/overlap during maintenance. Back up, inventory both receipt
tables/decision ledger, broker active/in-flight/retry/dead-letter copies and
downstream effects, and reconcile in source order. The database migration locks
both receipt tables and refuses if *any* sample lacks delivery or completion, or
*any* entry lacks delivery, version 1 or completion (including non-Singles).
This intentionally blocks unknown version-0 and conflicting/delivered-but-
unprocessed receipts rather than guessing, backfilling, redriving, or deleting.
Current old workers do not supply all these marks; ordinary existing installations
may therefore be blocked and require authoritative manual reconciliation. If
the original decisions/config set/order cannot be established, stop. Do not
force completion or upgrade versions based on delivery. There is no automatic
reconciliation tool and no extra approval flag.

For an initial inventory (not proof of safety):

```sql
SELECT id, transponder_id, sample_id, delivered_at_utc, entries_created_at_utc
FROM statistics.sample_complete_outbox
WHERE delivered_at_utc IS NULL OR entries_created_at_utc IS NULL
ORDER BY transponder_id, id;

SELECT id, transponder_id, entry_id, single, delivered_at_utc,
       multi_decision_version, multi_decisions_created_at_utc
FROM statistics.entry_created_outbox
WHERE delivered_at_utc IS NULL OR multi_decision_version <> 1
   OR multi_decisions_created_at_utc IS NULL
ORDER BY transponder_id, id;
```

Rows passing this conservative check remain untouched, including their legacy
`DeliveredAtUtc` and NULL enqueue marker; no fabricated SMB message is generated.
The database check alone cannot prove broker quiescence, downstream success,
payload consistency or correctness of historical markers: explicitly resolve
all in-flight/queued/dead-letter duplicates and effects before activation.
Completed legacy redeliveries must validate against retained receipts and return
without effects, release or enqueue. Missing/ambiguous/conflicting legacy
receipts or unprovable ordering remain quarantined. Never infer order from event
times. Older activation requirements in `SampleIdentityPersistence.md`,
`EntryReplay.md` and `MultiCreationDecisions.md` still apply; do not rerun or
rewrite an applied destructive activation migration.

An existing `statistics_processor` schema causes Up to fail rather than adopting
unknown objects. Up is transactional; indexes are built nonconcurrently while
receipt tables are locked. Schedule maintenance and account for table size/lock
timeouts. Migration locks cannot prevent an old writer queued behind the lock
from resuming afterward; the operational stop is mandatory. Deploy schema before
the converted code, then start exactly one processor/poller only after all
maintenance and worker/hosting acceptance passes. No changes were applied to a
shared database by this prerequisite.

Down locks both receipts and refuses if *any* receipt remains, even a completed
legacy receipt. It also uses `DROP SCHEMA ... RESTRICT`, which refuses if SMB
created any object; it never cascades into SMB or touches the API outbox. Prefer
roll-forward. Stopping workers alone is not safe rollback: retained enqueue state,
broker copies and effects require reconciliation and a consistent backup.

## Handoff tests

Tester: use real PostgreSQL and SMB; verify fresh/upgrade migration, precondition
failure with atomic rollback, schema isolation/privileges and existing history;
head order, two riders/two streams, both insert/completion races, rollback at
every publish/mark/effect boundary, restart, delivery-mark failures, aborted SMB
rows, late duplicates after cleanup, poison/DLQ repair, and no-result/empty-set
Multi decisions with config edits, deletion, equal/backdated parents and Single
semantics. Exercise conflict/missing/legacy source failures and concurrent replay.
The schema tests do not prove worker logic or Azure settlement/session behavior;
SQLite does not prove PostgreSQL locking, indexes or transaction behavior.
