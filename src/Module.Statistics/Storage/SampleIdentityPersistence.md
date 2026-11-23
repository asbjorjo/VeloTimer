# Statistics sample identity: expand phase and handoff

This migration is **storage preparation**, not activation of historical data. The
new processor writer and its isolated transactional completion outbox are documented
in `SampleCompleteOutbox.md`. Legacy unkeyed samples and downstream entry processing
remain unsafe for replay until their separate activation and consumer work is complete.
No message contract changed.

## Schema and writer contract

`20261123131028_statistics_sample_timing_identity` adds nullable original Timing
point IDs to `statistics.sample`. They are not Facilities course point IDs and are
not foreign keys to another service's database. Both must be null (unkeyed) or both
non-null; PostgreSQL enforces this with `ck_sample_timing_points_pair`. The
transactional, partial unique index `ux_sample_timing_identity` enforces at most one
keyed row for `(transponder_id, timing_point_start_id, time_start,
timing_point_end_id, time_end)`, including concurrent inserts. It does not prevent
any number of unkeyed duplicates. SQLite's test model has a non-partial unique index
but does not implement the PostgreSQL pair check.

The Coordinator confirmed this tuple is unique for supported `TimingSampleComplete`
producers **after** both times use `StatisticsStoragePrecision.Timestamp` and the
existing UTC-instant conversion: Local -> UTC, Unspecified interpreted as UTC. New
writers must perform that conversion and normalization before lookup, insert,
enrichment and payload comparison; keep source points and endpoint times immutable
after creation. This includes the PostgreSQL epoch truncation rule, not naive rounding
or truncating ticks from year 1. SQLite writers must also normalize to this agreed
identity, even though its store retains 100ns ticks. Compare other immutable payload
and enrichment as agreed by the messaging owner; do not silently merge conflicts.
Handle PostgreSQL unique violations by rolling back the failed transaction and
reloading/validating the winning committed row. The index is not an outbox.

Migration Up neither infers IDs nor deletes or updates any historical samples, entries,
checkpoints, outbox records or other rows. It is an expand-only mixed-version
transition; old writes remain unkeyed. EF history remains `ef.__ef_migrations_history`;
the earlier Statistics history migration added no history tables/triggers. Index creation
is deliberately **transactional, not concurrent**, so schema, check and index are
atomic; it scans the sample table and can block writes. Schedule maintenance, provision
table-owner DDL privileges and adequate lock wait/IO/disk. No shared database was
migrated. Down refuses while any keyed sample exists, since dropping IDs loses
provenance; stopping keyed writers and restoring a consistent backup is safer than
discarding IDs. Do not roll back to the old handler and claim replay safety.

## Required activation and reset decision — not implemented

Legacy samples lack original Timing point IDs. Course point, times, facility and
distance do not establish a reliable mapping: Facilities associations can change and
more than one source point may map to a course point. There is no cross-service DB
join or guessed backfill here. Because entries lack persisted sample/contributor
lineage and Multi uses windows of Simple entries, selecting only entries linked to
unkeyed samples cannot be proven complete. A bounded **whole Statistics-derived
results** reset candidate is deleting every row from `statistics.statistics_entry`
(both Simple and Multi, including manual invalidation audit/validity and distance
snapshots), then every row from `statistics.sample`, in one transaction. This also
removes any keyed samples already produced during the transition. Do not execute this
candidate automatically: Coordinator must confirm external dependencies, exact scope,
reconciliation and deployment plan first. No definitions, configs, profiles,
Timing passings/samples, Facilities data or `statistics.smb_outbox` rows are in that
candidate. Revalidation checkpoints reference definitions and contain entry cursors;
coordinate their safe rescheduling/reset with the scheduler owner rather than delete
or casually rewrite them. Inventory actual database FKs/triggers and dependencies before
any reset; fail closed if the deployed schema or consumers have additional dependencies.

Deletion is irreversible without a tested backup. It changes visible results, invalid
and manually audited records, leaderboard/hall-of-fame output and previously published
events; it cannot retract events or downstream side effects. Reset alone does **not**
regenerate results. Timing retains source passings/samples, but this repository provides
no established comprehensive ordered historical replay tool. Even with one, historical
Facilities association/layout availability, present definitions and thresholds,
ordering, missing or expired messages, and missing manual audit state mean regenerated
results may differ or be incomplete. Restore from backup if required fidelity cannot
be established. Results and downstream projections may need explicit reconciliation;
their loss or modification outside Statistics needs separate scope approval.

Before any activation: inventory both unkeyed and keyed rows, derived entries, manual
audit state, checkpoint generations/cursors, caches, all broker subscriptions/dead
letters/in-flight completions and outbox delivery states; take and test backups.
Stop old/new Statistics sample and entry writers, API changes affecting definitions,
revalidation workers, and relevant consumers/dispatchers under an agreed maintenance
barrier; ensure no already-running transaction can resume after the barrier. Coordinate
queued `TimingSampleComplete`, `SampleComplete`, `EntryCreated` and threshold events
with their owners. In particular, do not clear shared `statistics.smb_outbox` or assume
presence/absence proves delivery; pending or already-delivered events may refer to
deleted state. Drain, quarantine or reconcile individually only under the messaging
owner's approved plan, preserving unrelated threshold events.
Also inventory and reconcile `statistics.sample_complete_outbox` before any reset:
its pending payloads can refer to samples being deleted, and retained delivered rows
do not establish downstream success. Do not truncate it as an automatic reset step.

Deploy the expand migration first; finish and verify replay-safe sample/outbox and
Simple/Multi consumers before enabling any replay. A later coordinated activation
migration must, under the same writer barrier, either reliably reconcile **all**
unkeyed rows and their derived dependencies or perform the approved reset, verify no
null source point IDs remain, and enforce both columns NOT NULL. Writers that omit
them must remain stopped permanently. The final index/check and snapshot must be
reviewed with that migration; this expand migration itself is not that activation.
Restart only compatible consumers after broker/outbox reconciliation, clear or refresh
appropriate caches, monitor pending/retry/dead-letter queues and checkpoint completion,
and execute only an explicitly verified replay in per-transponder order. A replay-safe
outbox guarantees durable at-least-once delivery, not exactly-once transport.

Tester: exercise migration on representative PostgreSQL data with concurrent legacy
and keyed writers, failed/rolled-back races, both sides of the 2000 epoch, timestamp
ties and distinct point IDs; final reset, in-flight/pending/aborted outbox and rollback
boundaries require integrated tests with messaging and scheduler owners.
