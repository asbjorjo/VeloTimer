# Validity storage handoff

Storage and processing handoff. Entry creation and historical revalidation use the
window semantics below. Do not enable threshold saves without the transactional
scheduling protocol. Multi validity has no persisted contributor links or proof flags.

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
  measurements or window endpoints.
* `Speed` remains non-nullable `double`. PostgreSQL `double precision` and Npgsql retain
  Infinity/NaN; zero elapsed time can retain the existing calculated Infinity and
  negative elapsed time its original negative speed. No arbitrary speed or nullable
  contract change is needed for this storage. Classify all nonpositive durations
  invalid regardless of bounds. Never serialize nonfinite values as ordinary JSON
  numbers: public reads must exclude stored invalid rows before projection/paging;
  existing `EntryCreated` transports only IDs/times, not speed. If a future unfiltered
  transport must expose speed, return to the contract owner before changing its type.
* Immutable `20261103135514_statistics_validity_thresholds` is transactional, adds
  compatible defaults, initially empty contributor/checkpoint tables and a configuration-item lookup index.
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
* `20261104085927_statistics_remove_contributor_lineage` is an additive forward,
  transactional migration. It drops the unused `statistics_entry_contributor` table
  (including its constraints/indexes/data) and the entry
  `has_complete_contributor_lineage` column. All measurements, validity classifications,
  ownership, definitions and other data are retained. Earlier migrations/designers
  remain immutable; fresh installations apply their original schema then this removal.
* The removal migration also upserts checkpoints for **every existing item**, ordered
  by UUID: new rows get generation=1; existing rows increment generation by one,
  including idle jobs. All become pending, phase=Simple (0), cursor=null. This broader
  metadata-only reset includes every Multi item and its parent/dependent items, even
  if bootstrap previously completed or thresholds never changed. It reads definitions
  and updates small checkpoint rows only, never scans entries or classifies results.
  Generation overflow fails and rolls back the transaction rather than wrapping.
  Reapplying an already-recorded migration does not schedule another sweep.
* EF history remains `ef.__ef_migrations_history`. The existing history-schema
  migration is empty: no row-history triggers or cross-service storage were added.
  Generated designer/snapshot `public.pg_trgm` annotations were corrected because
  the current Npgsql scaffolder omits the extension schema.
* `20261104094426_statistics_coalesce_multi_rescan` adds the non-null boolean
  `multi_rescan_pending` with constant default false. It does not reset generations,
  phases, cursors or pending states, scan entries, or change measurements/validity.
  Bootstrap inserts omit this column and receive the same false default.
  Apply before deploying the matching scheduler/worker; stop old schedulers/workers
  so they cannot keep restarting active Multi sweeps or ignore requested follow-ups.
  Down only drops the flag and would lose queued follow-ups; it was not executed.

Deployment requires PostgreSQL 11+ fast constant defaults, table-owner/index/DDL
privileges, the existing `public.pg_trgm` prerequisite, and allowance for short
exclusive DDL locks and the measured concurrent-index build. The configuration table
checks/index scan definitions, not historical measurements. Take a backup, including
unused provenance if rollback recovery matters. Stop Statistics APIs/entry writers and
revalidation workers during this migration, apply all pending migrations, then start
only the matching new binaries. Old binaries selecting/writing the removed column/table
are incompatible: this is not a mixed-version rolling schema change. Allow exclusive
DDL locks and checkpoint-row locks for the metadata reset; total work is proportional
to item count, not entry count. Restart polling workers to drain the persisted pending
jobs; no outbox event is needed. Stored classifications remain authoritative until
bounded asynchronous processing converges, including normal post-commit cache invalidation.
The removal migration's Down recreates the empty contributor table with its original
keys/check/indexes and a false-default flag; it fabricates no links and does not restore
deleted provenance or reverse checkpoint generations/scheduling. Earlier Down migrations
remove bounds/classification/checkpoints. **No Down was executed; rollback requires
explicit approval and cannot recover provenance without a backup.**

## Current Multi window contributors

For **every** Multi, contributors are the current Simple entries matching its parent
configuration, transponder and facility, fully contained by inclusive comparisons:
`parent.TimeStart >= multi.TimeStart AND parent.TimeEnd <= multi.TimeEnd`.
The count must equal configured repetitions (positive), and every matching entry
must be valid. Unexpected count or any invalid parent means `InvalidContributor`
(after the evaluator's existing nonpositive-duration precedence). The Multi's own
effective duration bounds still apply. Keep the raw total; never choose a subset,
skip invalid laps, require endpoint equality, or reject an unrelated partial overlap.
Contained overlapping/tied rows count individually, including extras arriving later.

New Multi endpoints still come from the latest N parent entries of the transponder/
configuration with `TimeEnd <= triggering end`, ordered by TimeEnd then Id descending,
without filtering invalid entries. The oldest selected start and newest selected end
define the window, and the newest selected facility is retained. This selection only
defines the window: classification uses the same containment query as historical
processing, so cross-facility selection or extra contained rows can invalidate it.
Insufficient parents still produce no new Multi.

Both creation and historical processing use one grouped SQL query over bounded Multi
IDs, joining their configurations and parent entries. SQL returns only count/invalid
count per Multi, not all parent measurements; an empty join is invalid. No per-Multi
queries, original-ID reconstruction, or separate historical-row policy is used.
Creation inserts and classifies inside the existing caller transaction, saving final
validity before commit/publication. No creation events are emitted on reclassification.

Processing and the current EF model neither read nor write contributor links or
completeness flags. The forward removal migration removes their schema and stored data;
there is no link guarantee for new or historical entries.
Old diagnostic result fields/counters are retained for caller compatibility but
always receive zero; they no longer classify rows into distinct processing paths.

## Durable bounded checkpoint protocol (API/domain/Workers owners)

`StatisticsValidityRevalidation` is one small coalescing row per **item**, even for
configuration-scoped events. Fields: item PK/FK, positive `Generation` (EF concurrency
token), `IsPending`, `Phase` (Simple=0/Multi=1), nullable `LastEntryId` UUID and
`MultiRescanPending` (default false). No per-entry
work queue, job framework, revision-based public gate or millions-ID event is needed.
The pending/item index supports polling; the entry item/id index supports batches.

1. All definition and entry writers establish an item checkpoint row if absent
   (`INSERT ... ON CONFLICT DO NOTHING`, pending when absent) and lock it in their
   Statistics transaction **before** reading definitions/classification. Multi entry
   writers use `FOR SHARE`; definition writers and revalidation use `FOR UPDATE`.
   Simple creation locks the owning and dependent item scope in one UUID-ordered
   statement, exclusively when dependencies will be scheduled (otherwise shared).
   Config edits for the same item use this protocol too. Retry
   PostgreSQL serialization/deadlock failures; do not hold a broker lock for a sweep.
2. On a real threshold save, validate and save definitions, increment Generation,
   set pending=true, phase=Simple, cursor=null, MultiRescanPending=false, and enqueue the agreed
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
4. Exhaust Simple, reset cursor and move to Multi. Exhaust Multi and mark idle only
   if MultiRescanPending=false; otherwise reset the cursor, clear that flag and stay
   pending in Multi for one coalesced follow-up pass. Do not clear the flag at ordinary
   batch boundaries or restart an active pass when another notification arrives.
   Duplicate/out-of-order threshold messages merely wake the current durable job,
   **not** create a new generation or replay obsolete definitions. Retain idle job
   rows so delayed notification/dedup does not depend on an unbounded event-ID table.
   Parallel pollers must lock/re-read the job, not reuse a pre-lock cursor.
5. Parent validity changes must schedule distinct dependent Multi **items** durably
   in the same transaction as the parent batch, incrementing their generation without
   resetting an active sweep cursor. A pending Simple phase already has a later full
   Multi pass, so needs no extra rescan. A pending Multi phase sets MultiRescanPending=true,
   coalescing any number of arrivals into a follow-up pass after reaching the tail.
   Idle jobs become pending Multi at cursor=null with no extra rescan flag.
   Resolve dependencies from configuration definitions. Same-item
   dependencies are already covered by that job's later Multi phase; do not reset
   its own Simple cursor. Lock dependent item rows consistently. Even if a child job
   ran early, the final parent change schedules another sweep. Revalidation never
   emits EntryCreated, so this does not recreate aggregates.
6. New Simple entries read live bounds under the item lock and classify before save.
   Every successful Simple creation also schedules dependent Multi items atomically
   with insertion, including same-item dependencies and **even valid new parents**.
   Scheduling increments Generation and sets pending; an already-pending Simple phase
   keeps its cursor and later runs Multi from the beginning. An active Multi keeps
   its cursor and requests a follow-up pass; only idle jobs start at cursor null.
   This covers late parents newly contained by an old window, even
   behind a completed UUID cursor. An overlapping noncontained row does not contribute.
   New Multi entries read current containment counts/validity and own bounds under
   their item lock. Later parent validity changes schedule dependent jobs as above.
   If a child batch runs first, the parent transaction waits on the same row lock,
   then requests a follow-up or wakes the now-idle job. Completion, consuming the flag,
   validity and checkpoint changes are atomic; a notification arriving behind the
   completion lock cannot be lost. Arrivals during a follow-up request another pass
   without resetting that pass's cursor. Insertion or scheduling failure rolls back
   both; no new bus event is needed.
   Cached definitions or a UUID snapshot cursor alone cannot guarantee convergence.
7. On this rollout, stop old binaries and apply the removal migration's all-item
   metadata reset before starting current writers/workers, as detailed above.
   Bootstrap still establishes missing jobs for later items, but does not reset
   existing idle jobs; it is not a replacement for the migration reset. Workers own
   bounded entry classification and cache invalidation, not the schema migration.

The schema does not enforce cooperating-writer behavior by itself. Stored IsValid
alone remains read authority; public results converge as pending jobs complete.

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
  microsecond TimeSpan materialization, original CHECK categories, stale generation
  rejection and checkpoint recovery using new DbContexts.
* Follow-up PostgreSQL checks proved new-row false validity is not overwritten by
  the true database default, negative duration/speed remain retained, omitted fields
  retain compatible defaults, and repeated EF update applies no migrations.
  Outbox registration and real transaction checks are detailed in OutboxPersistence.md.

The preceding original-schema checks are historical evidence, not current contributor
requirements. Current removal/upgrade checks are recorded below.

Tester: add maintained PostgreSQL migrations/model tests and representative-volume
EXPLAIN/keyset checks; test submicrosecond rounding, negative durations, all four reasons,
new invalid-row insertion/default handling, exact containment count/validity,
facility/config/transponder exclusions, global/local concurrency, failed-index recovery
and limited DDL roles.
After API/Workers implementation, test crash/retry/resume, generation supersession,
duplicate/out-of-order wakeups, late inserts on both sides of the UUID cursor, dependency
propagation, cache invalidation and no duplicated Multi creation.

## Processing simplification validation (2026-11-04)

No entities, migrations or repository tests were edited. Release processor build
(`dotnet build src/Module.Statistics.Processor/VeloTime.Module.Statistics.Processor.csproj
--configuration Release --no-restore`) also built core: **0 warnings/errors**.
Unchanged Statistics domain tests: **22 passed**. Unchanged Workers tests excluding
the four conflicting cases below: **192 passed, 14 skipped, 0 failed** with
`OIDC_AUDIENCE=https://build.invalid` and `VELOTIME_TEST_STATISTICS_POSTGRES` pointing
at an isolated PostgreSQL 17 container. Agent-outbox checks lacked their separate
PostgreSQL setting and were skipped. Existing dependency advisory warnings remain.
Both test commands used Release, `--no-restore --logger trx /p:CollectCoverage=true
/p:CoverletOutputFormat=lcov`.

A temporary external probe (`/tmp/velotime-window-processing-probe/Probe.csproj`,
not maintained tests) passed **22 assertions** against the same isolated PostgreSQL:
creation/revalidation agreement; facility/transponder/partial-overlap exclusions;
invalid parent inclusion; extra contained parents invalidating both new and old Multi;
raw totals retained; no link writes; 128 windows in exactly one grouped query;
insertion/scheduling rollback; a late writer waiting for a locked Multi batch then
restarting its cursor; same-item dependencies preserving pending Simple progress and
restarting completed jobs. The captured contributor SQL uses `id = ANY(@ids)`,
joins Multi configuration, Simple parent configuration and parent entries, groups
by Multi ID/repetitions, and returns `count(*)` plus
`count(*) FILTER (WHERE NOT parent.is_valid)`. Parent filters are:

```sql
parent.statistics_item_config_id = config.parent_config_id
AND parent.statistics_item_id = parent_config.statistics_item_id
AND parent.transponder_id = multi.transponder_id
AND parent.facility_id = multi.facility_id
AND parent.time_start >= multi.time_start
AND parent.time_end <= multi.time_end
```

The page query remains `WHERE statistics_item_id = @item [AND id > @cursor]
ORDER BY id LIMIT @limit`, with PostgreSQL UUID ordering and no OFFSET. Only validity
fields are updated; checkpoints retain generation guards, restart and transaction behavior.

Tester must replace these obsolete assertions in
`src/VeloTime.Workers.Test/StatisticsValidityPostgreSqlTests.cs` (a separate unchanged
test run confirmed **4 failures**, not an all-green full suite):

* `CreationPrecisionMultiAtExactMaximumRetainsFixedLineageAndStableMeasurements`:
  fails line 136 (expected valid, actual invalid); samples use distinct facilities
  at line 118. Use one facility for this precision scenario; remove link/flag assertions
  at lines 142-144 and 155-158, preserving all precision/measurement assertions.
* `MultiUsesFixedInvalidConstituentsAndParentPropagationRestoresWithoutNewAggregates`:
  fails line 280 (expected valid, actual invalid); the Entry helper at line 607 creates
  different facilities. Use matching facilities; replace link expectations at lines
  281-282, 293-294 and 303 with window validity/retained-row checks.
* `InvalidLapIsRetainedAndNeverSkippedToAssembleMulti`: fails line 330 (expected two
  contributor IDs, actual empty). Use matching facilities so invalidity specifically
  tests the rejected contained lap, not a count mismatch; replace link assertions.
* `AmbiguousLegacyContributorsRemainConservativelyInvalidAndUnchanged`: fails line
  365 (expected unproven count 1, actual 0). Replace the proof-specific expectation
  with exact window counts. Current fixture gives A/C one facility and B another,
  so A/C are exactly two valid contained parents; all three in one facility instead
  makes the count three and invalid. Neither case depends on flags/links.

The SQLite domain aggregation fixture also mixes facilities but does not assert
validity; strengthen it to explicitly test the intended window semantics.
After Data & Migrations removes the unused schema, Tester must update migration/default/
constraint assertions too. Add maintained coverage for empty/too-few/too-many windows,
tied/overlapping rows, config/facility/transponder exclusion, late inserts on either
side of UUID cursors, failed insert/scheduling/classification commits, cancellation,
duplicate/out-of-order wakeups and concurrent parent/child sweeps. Existing message
contracts, headers, transponder sessions and creation delivery semantics are unchanged;
revalidation publishes no events and does not add aggregate deduplication guarantees.
The more conservative exclusive Simple-writer scope can increase item-level contention;
retain existing deadlock/serialization retry handling and measure representative load.

The data followup below supplies the schema removal and rollout reset. No large-volume
EXPLAIN validation is claimed by the processing-only checks above.

## Data followup validation (2026-11-04)

Changed only models, DbContext, new migration/designer/snapshot and storage/plan docs.
All earlier migration artifacts are unchanged. The Npgsql scaffolder again omitted
the extension schema; only the new designer and snapshot were corrected to preserve
`public.pg_trgm`.

* `dotnet build src/Module.Statistics.Migration/VeloTime.Module.Statistics.Migration.csproj
  --configuration Release --no-restore`: **0 warnings/errors** (also builds core).
* `dotnet build src/Module.Statistics.Processor/VeloTime.Module.Statistics.Processor.csproj
  --configuration Release --no-restore`: **succeeded, 1 warning, 0 errors**.
  Existing CS9113: unread `logger` in `TimingSampleHandler.cs:16`; OpenAPI generation
  also logged its existing container DataProtection-key persistence warning.
* `dotnet ef migrations has-pending-model-changes --project
  src/Module.Statistics/VeloTime.Module.Statistics.csproj --configuration Release
  --no-build`: **no pending changes**.
* `OIDC_AUDIENCE=https://build.invalid dotnet test
  src/VeloTime.Module.Statistics.Test/VeloTime.Module.Statistics.Test.csproj
  --configuration Release --no-restore --logger trx /p:CollectCoverage=true
  /p:CoverletOutputFormat=lcov`: **22 passed, 0 failed, 0 skipped**, unchanged tests.
* Disposable PostgreSQL 17 (`postgres:17-alpine`) databases `upgrade` and `fresh`:
  actual EF `database update` first migrated `upgrade` to
  `20261103135802_statistics_validity_revalidation_index`, then applied the removal.
  A temporary external Python/psql probe seeded idle Simple/Multi, pending Simple/Multi
  jobs with non-null cursors, and items missing jobs, including a cross-item Multi
  dependency. All six items became pending Simple with null cursors, generations
  incremented exactly once or inserted as 1. Repeated EF update left jobs unchanged.
* Seeded four entries (including UTC microseconds, zero-duration Infinity, negative
  duration/speed, retained invalidity and an already-processed valid Multi), two
  contributor links/true flag, bounded definitions and profile ownership. JSONB
  comparisons of every non-provenance column in entry/definition/profile/sample tables
  matched before/after; the sample table was empty. Only unused provenance was removed.
* Fresh full-chain migration succeeded. Both databases had the removal history row
  in `ef.__ef_migrations_history`, no contributor table/flag, valid entry indexes and
  the intentionally unvalidated entry validity CHECK.
* Generated reverse SQL was inspected, **not executed**: original empty contributor
  schema/indexes, false-default flag, no fabricated links and no checkpoint reversal.

EF 10.0.9 emits the existing runtime-10.0.12 version warning. Fresh empty databases
emit an initial missing history-table SELECT diagnostic before EF creates it; updates
exit successfully. The earlier public-index migration emits its existing concurrent
index partial-application warning; it was not rewritten. No live/shared database was
touched, and the disposable container was removed after checks.

Next owner: **Tester** updates obsolete worker fixtures/link/proof/schema assertions
listed above; removal means tests referencing deleted CLR members now need source
updates before that project can compile. Add maintained PostgreSQL forward/fresh,
reset/retention, migration atomicity/failure and worker convergence coverage. Verify
idle processed Multi reclassification after reset, dependency order, late inserts,
same-item/cross-item races and cache invalidation with current window semantics.
Representative-volume DDL lock timing, item-count reset load and EXPLAIN checks remain
unmeasured; deployment must quiesce old binaries as specified above.

## PR118 dependency coalescing followup (2026-11-04)

The protocol above now preserves active Multi progress under continuous Simple
insertions/parent reclassification. Each dependency arrival still increments Generation,
but requests a coalesced follow-up instead of restarting the active cursor. A job may
remain pending under continuous arrivals while successive passes keep reaching the tail.
Full threshold saves intentionally restart Simple and clear the follow-up flag.
The earlier temporary-probe descriptions of restarting an active Multi cursor are
historical, not the current behavior.

No repository tests were changed. Checks from this worktree:

* Release processor build (`dotnet build
  src/Module.Statistics.Processor/VeloTime.Module.Statistics.Processor.csproj
  --configuration Release --no-restore`) also built core: succeeded, 0 errors,
  existing CS9113 unread `TimingSampleHandler.logger` warning.
* Release migration-runner build: succeeded, 0 warnings/errors.
* EF `migrations has-pending-model-changes --project
  src/Module.Statistics/VeloTime.Module.Statistics.csproj --configuration Release
  --no-build`: no pending changes. Existing EF tool/runtime 10.0.9/10.0.12 warning.
* Unchanged Statistics domain tests: **22 passed, 0 failed, 0 skipped**.
* Unchanged Workers suite against isolated PostgreSQL 17: **198 passed, 1 failed,
  14 skipped**. Only failure is
  `MigrationModelConstraintsAndActualKeysetIndexAgree` line 784: expected migration
  count 10, actual 11. Tester owns updating it for this additive migration. Re-running
  with `--filter 'FullyQualifiedName!~MigrationModelConstraintsAndActualKeysetIndexAgree'`
  passed **198, 0 failed, 14 skipped** (Agent outbox PostgreSQL setting absent).
  Both test projects used Release, `--no-restore --logger trx /p:CollectCoverage=true
  /p:CoverletOutputFormat=lcov` and `OIDC_AUDIENCE=https://build.invalid`; the filtered
  run additionally used `--no-build`. Existing Workers restore/audit warnings remain.
* External disposable probe `/tmp/velotimer-pr118-rescan-probe/Probe.csproj`:
  **40 assertions passed** for same-item/cross-item active Simple preservation,
  active Multi and follow-up cursor preservation under repeated arrivals, tail and
  behind-cursor invalidation by an extra contained parent, single flag consumption
  at exhaustion, idle wake, failed exhaustion commit rolling back validity/cursor/
  flag, a late arrival waiting behind the completion lock then waking the job,
  and whole-threshold reset. This is diagnostic evidence, not maintained tests.
* The probe's `--upgrade` mode passed **4 assertions**: actual upgrade from the
  preceding migration preserves all fields of four idle/pending Simple/Multi jobs,
  supplies false flags, is idempotent on repeat, and matches the current model.
  Fresh full-chain migration also succeeded. No shared/live database or broker was
  contacted; no Down was run.

Next owner **Tester**: update the migration count and add maintained continuous-arrival
and follow-up tests for both dependency scopes; include arrivals before/after cursors
and after the completion lock, multiple pollers, failed/cancelled exhaustion transactions,
threshold supersession, and durable restart with a true rescan flag. Recheck duplicate/
out-of-order threshold wakeups leave generation/cursor/flag untouched. Contracts,
MessageName/EventId/item correlation, outbox boundaries, creation events and the grouped
current-window exact-count/all-valid query are unchanged; public reads still use stored
validity without revision gates. Coordinator should push/resolve only after Tester
verification.
