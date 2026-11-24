# Durable Multi creation decisions (storage prerequisite only)

`20261124095835_statistics_multi_creation_decisions` is additive PostgreSQL DDL.
StatisticsService, receipt writers, consumers and workers **do not yet implement**
this protocol. Do not activate the replay fix until the domain/worker integration
and PostgreSQL acceptance tests pass. No public DTO/message change is required:
the existing `EntryCreated.EntryId` resolves the source. An integration that cannot
resolve it authoritatively must fail, not introduce a breaking wire fallback.

## Exact local data contract

`statistics.multi_creation_decision` / `MultiCreationDecision`:

| EF field | PostgreSQL column/type | Meaning |
|---|---|---|
| `ParentEntryId` | `parent_entry_id uuid NOT NULL` | Exact triggering EntryId |
| `MultiConfigId` | `multi_config_id uuid NOT NULL` | Config ID considered at the original decision |
| `ResultEntryId` | `result_entry_id uuid NULL` | Original output EntryId; NULL is final no-result |

`pk_multi_creation_decision (parent_entry_id, multi_config_id)` is the unique
decision key. There is no pending ledger row and no update/upsert-overwrite
operation. Result is immutable in EF after insertion. Keys are immutable EF keys.
There are deliberately **no FKs**, including to configs, results or other services:
config/result/parent deletion cannot cascade into this ledger. Retain it and the
source/outgoing receipts independently of domain deletion. A deleted result must
not be recreated on replay; missing evidence that prevents safe handling must
fail explicitly. Deleted/retargeted configs never invalidate an existing decision.
Never delete decisions as part of config deletion, result cleanup or retention.
Database owners must restrict ordinary workers to the required INSERT/SELECT
ledger privileges (no UPDATE/DELETE); EF immutability is not a SQL security boundary.

Additional `EntryCreatedOutbox` fields:

* `MultiDecisionVersion` / `multi_decision_version integer NOT NULL DEFAULT 0`:
  0 means legacy/unknown; upgraded entry creators explicitly insert 1 in the
  original entry/receipt transaction, including derived receipts. EF rejects
  later mutation. Old writers continue inserting 0, so mixed deployments cannot
  silently opt old receipts into the protocol.
* `MultiDecisionsCreatedAtUtc` / `multi_decisions_created_at_utc timestamptz NULL`:
  NULL for pending version-1 work, otherwise UTC time at which the **entire**
  eligible config set and all outcomes committed. Set once, never clear/rewrite.
  Even zero eligible configs requires this marker; otherwise later config
  additions could create spurious outputs on replay.
* `ck_entry_created_outbox_multi_decision` accepts only versions 0/1 and prohibits
  a completion marker on version 0. Broker `DeliveredAtUtc` is unrelated.
* `ix_entry_created_outbox_transponder_creation_order (transponder_id, id)` is
  an unfiltered B-tree. The existing filtered pending dispatch index is retained.
  Existing bigint identity `Id` is the order token; `EntryId` remains uniquely indexed.

## Domain/worker owner: transaction, order and replay

Use the same scoped StatisticsDbContext, existing PostgreSQL transponder advisory
lock held through commit, and existing item/config lock/reload protocol. Direct
domain callers must also uphold it. Within that transaction:

1. Resolve the exact triggering entry and retained receipt; validate EntryId,
   rider, item, config, normalized UTC endpoints and `Single` semantics. An ID-less
   legacy alias needs the existing authoritative unique-source proof, never an
   arbitrary tuple match. Missing/ambiguous/conflicting sources fail explicitly.
2. Require version 1. If its completion marker is set, return/reuse its retained
   decisions/outgoing receipts, including no-result rows, **without consulting
   today's eligible configs or rerunning parent selection**. No row for a newly
   added config means it was not considered, not permission to create it now.
3. For pending version-1 work, lock/reload the current eligible configs. Resolve
   every counted parent to its retained EntryCreatedOutbox receipt. Restrict
   candidates to the same transponder and existing domain scope, with receipt
   `Id <= triggeringReceipt.Id`; never count a later-created parent, even when
   its event time is equal or earlier. Within the existing event-time window,
   order by `TimeEnd DESC, receipt.Id DESC` (not random EntryId). The trigger
   is included once by identity. Missing/unprovable receipt order is an error,
   not grounds to skip evidence or synthesize sequence IDs.
4. Insert exactly one ledger outcome per eligible config, including NULL when
   insufficient parents/no result. Insert any result, validity scheduling,
   ownership assignment and immutable outgoing receipt in the same transaction.
   Set the source completion marker even for an empty eligible set. Commit all
   together. Do not persist a pending decision, mark completion before inserts,
   or acknowledge a rolled-back/failed transaction.

Identity allocation under the shared transponder lock through commit makes receipt
IDs a **local creation order**, not event-time, broker-delivery or global commit
order. Sequence gaps from rollback are valid. Never renumber/reseed IDs or infer
order from UUIDs/delivery marks. Retain delivered receipts in candidate queries.
Consumers must not overtake an earlier same-rider source that still needs Multi
processing (respect `Single` routing); the worker owns that processing barrier.

Concurrent attempts serialize on the transponder lock. The composite PK is the
last integrity defense, not a substitute for locking: a uniqueness conflict
requires transaction rollback/retry and authoritative read of the committed
marker/outcome, not `ON CONFLICT DO UPDATE` or an unconditional success ack.
With NULL marker plus pre-existing decisions, fail as inconsistent unless a
separately approved reconciliation proves the entire original config set.

## Migration, legacy and activation gates

Up never deletes or guesses data: all existing receipts receive version 0/NULL;
no decisions are backfilled from results, timestamps or delivery status. **A
version-0 receipt with no decisions is unknown, not a first attempt or no-result.**
It must fail/quarantine on integration, including delivered receipts and replay
after config changes. No automatic promotion to 1 is safe. Inventory retained
receipts/entries, broker queues/retries/dead letters and downstream effects under
a stopped-writer maintenance barrier. Reconcile each legacy source using
authoritative history, or use the separately approved preproduction procedure in
[SampleIdentityPersistence.md](SampleIdentityPersistence.md). If original decisions
and considered config set cannot be proven, stop; this migration supplies no
reconciliation/reset tool and authorizes no deletion.

The earlier destructive sample-identity activation remains unchanged and requires
its explicit approval, both outboxes empty, stopped old writers and queue barriers,
even on fresh install. This later additive migration must not be used to bypass it.
If this schema has already been used, any separately proposed reset must inventory
and retain/reconcile this ledger too; do not rerun an applied reset migration.
No existing history table is rewritten; this ledger is itself retained decision
history, independent of mutable config/history cleanup.

Apply via the Statistics migration runner (`ef.__ef_migrations_history`) before
upgraded writers/consumers, with normal DDL and new table/column/sequence DML
privileges. Up is transactional; its nonconcurrent index build and table alterations
can block receipt writes, so schedule maintenance and set operational lock/timeouts.
Stop all old processors before integration activation. Down locks both tables and
refuses while any decision or version-1 receipt remains, even with no-result/empty
config set. Prefer roll-forward; neither migration was applied to a shared database.

Tester/next-owner acceptance: equal-time distinct parents delivered/replayed in both
orders; later backdated parents; delivered receipts in ordering; no-result then later
parents/repetition edits; empty config set then new config; deleted/retargeted configs
and deleted outputs; version-0/missing receipts; no marker with ledger rows; concurrent
same-source attempts under transponder lock; transaction rollback at each insertion/
marker/publication boundary; restart and uncertain broker acknowledgement. SQLite
does not prove PostgreSQL locking, sequence, constraint or transaction behavior.

## Storage validation

`StatisticsMultiDecisionMigrationTests` uses
`VELOTIME_TEST_STATISTICS_POSTGRES` as an administrative connection, creates and
drops its own uniquely named disposable database, and executes the generated
PostgreSQL Up/Down commands against the preceding receipt schema (not
EnsureCreated). It verifies legacy state preservation, no-result retention,
composite uniqueness, check constraints, atomic rollback, guarded downgrade,
EF result immutability and delivered-receipt index/order behavior. It does not
claim a full migration-chain/reset deployment or domain/worker concurrency test.
