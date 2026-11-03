# Validity storage handoff

Storage only; API, entry creation, worker hosting and public filtering are **not**
implemented here. Contract baseline: `bf02a3a9`. Do not enable threshold saves without
the transactional scheduling protocol below.

## Model and migration

* Item and base Simple/Multi configuration bounds are nullable PostgreSQL `interval`.
  Null means unbounded/inherited, not zero. Preserve requested local bounds; effective
  minimum is the maximum of global/local minima, effective maximum their minimum.
  PostgreSQL checks enforce positive individual bounds and each row's minimum <= maximum.
  API must validate intersecting global/local ranges, including all configurations
  affected by a global edit, under the same item lock. Inclusive evaluation belongs
  to the domain/worker owner, not a computed column.
* Measurements, UTC `timestamptz` start/end, SI distance/speed, duration and ownership
  remain unchanged. Npgsql intervals/timestamps persist at microsecond precision;
  UI/API must preserve canonical persisted precision, including untouched saves.
* `StatisticsEntry.IsValid` has CLR/database default true; `InvalidReason` is nullable
  integer using the agreed enum values 0..3. The check requires valid/no reason or
  invalid/a known reason. It does **not** require positive measurement duration:
  zero/negative measurements must remain stored and be classified asynchronously.
  Evaluate NonPositiveDuration, InvalidContributor, BelowMinimumDuration, then
  AboveMaximumDuration. Reclassification changes validity only, not creation events,
  measurements or contributor selection.
* `Speed` remains non-nullable `double`. PostgreSQL `double precision` and Npgsql retain
  Infinity/NaN; zero elapsed time can retain the existing calculated Infinity and
  negative elapsed time its original negative speed. No arbitrary speed or nullable
  contract change is needed for this storage. Classify all nonpositive durations
  invalid regardless of bounds. Never serialize nonfinite values as ordinary JSON
  numbers: public reads must exclude stored invalid rows before projection/paging;
  existing `EntryCreated` transports only IDs/times, not speed. If a future unfiltered
  transport must expose speed, return to the contract owner before changing its type.
* `20261103135514_statistics_validity_thresholds` is transactional, adds compatible
  defaults, empty lineage/checkpoint tables and a configuration-item lookup index.
  No historical entries or contributors are recalculated/copied. Entry validity CHECK
  is added **NOT VALID**, avoiding a synchronous historical table scan while enforcing
  subsequent writes. An operator may later `VALIDATE CONSTRAINT` in a separately
  approved maintenance operation; its unvalidated catalog state is intentional.
* `20261103135802_statistics_validity_revalidation_index` builds `(statistics_item_id,id)`
  **CONCURRENTLY** for ascending UUID keyset scans. It is separate so the schema
  migration is recorded before the nontransactional build. On failure, inspect
  `pg_index`; an approved recovery drops only this migration-owned index concurrently
  (whether INVALID or completed without history) before retrying the pending index
  migration. Do not use `IF NOT EXISTS` to accept an unknown/invalid index.
* EF history remains `ef.__ef_migrations_history`. The existing history-schema
  migration is empty: no row-history triggers or cross-service storage were added.
  Generated designer/snapshot `public.pg_trgm` annotations were corrected because
  the current Npgsql scaffolder omits the extension schema.

Deployment requires PostgreSQL 11+ fast constant defaults, table-owner/index/DDL
privileges, the existing `public.pg_trgm` prerequisite, and allowance for short
exclusive DDL locks and the measured concurrent-index build. The configuration table
checks/index scan definitions, not historical measurements. Take a backup. Apply both
new migrations before enabling workers. Generated Down removes bounds, classification,
lineage and checkpoints: **destructive, not executed, requires explicit approval**.

## Exact Multi lineage

`StatisticsEntryContributor` has owner/position primary key, unique owner/contributor,
indexed contributor FK, nonnegative position and no self-reference. Owner deletion
cascades its links; contributor deletion is restricted so provenance cannot disappear.
API deletion guards must consider retained invalid rows and dependent Multi entries.

New Multi creation must save the **actual selected parent IDs**, newest-first positions
0..N-1, and `HasCompleteContributorLineage = true` atomically with the entry. Do not
filter invalid parents out before selecting the window. Check flag, contiguous count
equal to configured repetitions, parent configuration, transponder and each contributor's
stored validity; the flag alone is not evidence. These cross-row invariants are the
domain/worker's responsibility, not enforced by a CHECK. Do not mutate links during
revalidation. A deterministic TimeEnd/Id tie-breaker for new selection is recommended;
save whichever exact ordered selection is actually used.

Legacy flag defaults false; no IDs are inferred by migration. Current selection is
latest N parent entries by TimeEnd, with no tie-breaker, and ignores the triggering
message's start/end when querying. Concrete unrecoverable case: A=[0s,10s] and
B=[10s,20s] originally produce a two-lap Multi=[0s,20s]. A later parent C=[0s,15s]
would now be selected with B, yielding the same Multi start/end but replacing A.
Equal TimeEnd ties are another ambiguity. Replaying today's window cannot prove
original contributors. **Coordinator must approve legacy handling** before worker
rollout; proposed conservative classification is InvalidContributor when provenance
cannot be proven, never silently substituting rows or declaring uncertain Multi valid.
This storage phase preserves legacy validity until that asynchronous decision.

## Durable bounded checkpoint protocol (API/domain/Workers owners)

`StatisticsValidityRevalidation` is one small coalescing row per **item**, even for
configuration-scoped events. Fields: item PK/FK, positive `Generation` (EF concurrency
token), `IsPending`, `Phase` (Simple=0/Multi=1), nullable `LastEntryId` UUID. No per-entry
work queue, job framework, revision-based public gate or millions-ID event is needed.
The pending/item index supports polling; the entry item/id index supports batches.

1. All definition and entry writers establish an item checkpoint row if absent
   (`INSERT ... ON CONFLICT DO NOTHING`, explicitly idle for mere entry creation)
   and lock it `SELECT ... FOR UPDATE` in their Statistics transaction **before**
   reading definitions/classification. Config edits for the same item use this lock
   too. Use a consistent item-ID lock order for multi-item operations and retry
   PostgreSQL serialization/deadlock failures; do not hold a broker lock for a sweep.
2. On a real threshold save, validate and save definitions, increment Generation,
   set pending=true, phase=Simple, cursor=null, and enqueue the agreed
   `StatisticsValidityThresholdsChangedV1` via the same scoped StatisticsDbContext
   active PostgreSQL transaction, then commit. Configuration ID remains present
   for config events and null for item events; storage intentionally scans the whole
   item in either case. No-op saves must not reset jobs. Invalidate caches after commit.
3. Worker polls durable pending rows at startup and thereafter (event is a wake-up
   hint, not the only source of work). Each invocation processes a bounded batch
   with a short transaction: lock job, read **current persisted** thresholds and
   entries, check expected Generation, update validity and checkpoint together,
   commit, release. SQL keyset: item equality, `id > last_entry_id`, ascending UUID,
   LIMIT; no OFFSET. Use PostgreSQL UUID ordering, not a string/.NET sorting surrogate.
   Reject results computed before the locked/current definition read. Generation
   concurrency failures discard the entire batch; never checkpoint a rolled-back batch.
4. Exhaust Simple, reset cursor and move to Multi; exhaust Multi and mark idle.
   Duplicate/out-of-order threshold messages merely wake the current durable job,
   **not** create a new generation or replay obsolete definitions. Retain idle job
   rows so delayed notification/dedup does not depend on an unbounded event-ID table.
   Parallel pollers must lock/re-read the job, not reuse a pre-lock cursor.
5. Parent validity changes must schedule distinct dependent Multi **items** durably
   in the same transaction as the parent batch, resetting their generation/cursor.
   Include legacy dependent configurations, not only recorded join links. Same-item
   dependencies are already covered by that job's later Multi phase; do not reset
   its own Simple cursor. Lock dependent item rows consistently. Even if a child job
   ran early, the final parent change schedules another sweep. Revalidation never
   emits EntryCreated, so this does not recreate aggregates.
6. New Simple entries read live bounds under the item lock and classify before save.
   New Multi entries save selected lineage and current own validity under their item
   lock, reading fixed contributors. All later contributor validity changes schedule
   the dependent job as above. This insertion/write protocol, **not a UUID snapshot
   cursor**, covers racing inserts behind an already-passed cursor. Cached definitions
   alone are not a safe current-threshold read. If any writer cannot follow the lock/
   classify/dependency protocol, Workers must design an explicit follow-up repair
   mechanism before claiming eventual convergence.
7. On rollout, upgrade/stop old entry writers before enabling processing and seed
   pending rows for **all existing items** asynchronously, even if no thresholds
   were edited, to classify historical nonpositive entries. Migration does not seed
   jobs or claim completion. Workers own this bounded bootstrap and cache invalidation.

No worker, scheduler, API transaction/publisher, polling service or lock helper is
implemented in this phase. The schema supports this protocol; it does not enforce
cooperating-writer behavior by itself. Stored IsValid alone remains read authority.

## Validation and next checks

Executed in the assigned worktree:

* Statistics core and migration-runner Release builds succeeded, 0 warnings/errors.
* `dotnet ef migrations has-pending-model-changes --project
  src/Module.Statistics/VeloTime.Module.Statistics.csproj --configuration Release`:
  no changes after rebuilding. Checked-in EF 10.0.9 warns that runtime is 10.0.12;
  tool manifest was intentionally not changed.
* Existing Statistics domain Release tests with CI logger/coverage options: final
  **8 passed, 0 failed, 0 skipped**. An initial 4-failure run exposed non-snake-case
  SQLite fixtures; raw SQL CHECKs are now PostgreSQL-only, like duration/extension
  behavior. SQLite proves mapping/regression compatibility, **not these checks**.
* A dedicated disposable PostgreSQL 17 container/database was migrated to the last
  existing migration, seeded with three legacy entries including zero-duration
  Infinity and a Multi, then upgraded through both new migrations. All measurements
  survived unchanged, defaults were true/null/false, both history entries were present,
  entry CHECK remained unvalidated and keyset index was valid.
* Temporary external console harness (not repository test code) checked UTC and
  microsecond TimeSpan materialization, exact ordered lineage, all CHECK categories,
  duplicate contributor rejection (23505), contributor deletion restriction (23503),
  stale generation rejection and checkpoint recovery using new DbContexts.
* Follow-up PostgreSQL checks proved new-row false validity is not overwritten by
  the true database default, negative duration/speed remain retained, omitted fields
  retain compatible defaults, and repeated EF update applies no migrations.
  Outbox registration and real transaction checks are detailed in OutboxPersistence.md.

Tester: add maintained PostgreSQL migrations/model tests and representative-volume
EXPLAIN/keyset checks; test submicrosecond rounding, negative durations, all four reasons,
new invalid-row insertion/default handling, lineage completeness/membership, contributor
FK delete effects, global/local concurrency, failed-index recovery and limited DDL roles.
After API/Workers implementation, test crash/retry/resume, generation supersession,
duplicate/out-of-order wakeups, late inserts on both sides of the UUID cursor, dependency
propagation, cache invalidation and no duplicated Multi creation.
