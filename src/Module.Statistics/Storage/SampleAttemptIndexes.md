# Sample attempt lookup indexes

## Scope and key order

`20261116132807_statistics_sample_attempt_indexes` is an additive, index-only
PostgreSQL migration. The existing sample PK is retained. All four new indexes
are nonunique ascending B-trees; no data backfill, constraints, timestamps, units,
contracts, processor, API or UI behavior change. Duplicate/conflicting/nonpositive
edges remain representable and the reader still fails closed where appropriate.

| Index (all names prefixed `ix_sample_`) | Keys in order | Purpose |
| --- | --- | --- |
| `transponder_completion` | transponder_id, time_start, time_end | Exact completion before facility is known; course-point equality and DISTINCT facility remain heap filters/aggregation. |
| `transponder_facility_start_time` | transponder_id, facility_id, course_point_start_id, time_start | Latest configured departure via backward LIMIT 1 scan. |
| `transponder_facility_end_time` | transponder_id, facility_id, course_point_end_id, time_end | Latest configured arrival via backward LIMIT 1 scan. |
| `transponder_facility_attempt` | transponder_id, facility_id, time_end, time_start | First overlap-evidence branch, including edges spanning the window. |

These equality prefixes differ deliberately. Putting facility before completion
endpoints cannot efficiently resolve unknown facilities. Putting point before
evidence times would require scanning every point's history. Evidence's second
branch (start within the attempt, including reversed edges) reuses the completion
index's transponder/start range. No extra start-range index, DESC twins, standalone
prefix indexes, INCLUDE payloads, uniqueness or partial positive-duration filter
are justified. Existing statistics-entry indexes are unchanged; entry replay
lookup is outside this sample-only change.

## Deployment and failure recovery

* Apply through the existing Statistics migration worker/job; EF history stays
  `ef.__ef_migrations_history`. Prior migrations have their own prerequisites;
  this migration does not install extensions or change `public.pg_trgm`.
* Requires table ownership/DDL privileges and enough disk, WAL and I/O headroom
  for four builds, plus transient build space. PostgreSQL scans the table twice
  per concurrent build and can wait on writers/old snapshots. Concurrent builds
  avoid blocking ordinary writes, not all locks or resource contention. Set the
  migration command/job timeout for production volume, not the small test timing.
* Up uses `CREATE INDEX CONCURRENTLY`, outside a transaction. Do **not** wrap the
  migration/script in an encompassing transaction. EF records this migration
  only after all four indexes succeed; it is not an atomic four-index operation.
  On interruption, inspect `pg_index.indisvalid/indisready`, definitions and EF
  history. With operator approval, drop only these migration-owned indexes
  **CONCURRENTLY** (including a completed prefix without a history entry), then
  retry the pending migration. Up deliberately has no `IF NOT EXISTS` to hide
  invalid or incompatible indexes.
* Down drops only these four indexes concurrently, outside transactions; no sample
  rows are removed. A failed Down can likewise be partial and needs inspection.
  The tested downgrade target is the immediately preceding required-points
  migration, **not** earlier schema/data-removal migrations.
* Old/new readers and writers are schema-compatible while these indexes build.
  This does not relax deployment restrictions of any older pending migrations.
  SQLite test `EnsureCreated` receives ordinary indexes and ignores Npgsql's
  concurrent annotation; PostgreSQL migrations are not run on SQLite.

## Measurement (PostgreSQL 17.11, isolated local Docker)

`StatisticsSampleIndexPostgreSqlTests.cs` contains reproducible seed SQL and all
four predicate/projection/order shapes from `RequiredCoursePointAttemptReader`.
It creates/drops its own random database, downgrades only the new index migration,
loads **1,000,000** interleaved rows (1,000 riders, 1,000 time slots, three
facilities, eight points; UTC instants), then VACUUM ANALYZEs. It compares default
planner `EXPLAIN (ANALYZE, BUFFERS)` before/after building on the populated table.
No planner switches force index use. Synthetic IDs/times are SQL constants, not
a benchmark of prepared generic plans, messaging or end-to-end processing.

One run on 2026-11-16 (execution time, excluding planning):

| Query | Before ms | After ms | After plan | Execution buffers before → after (hit + read) |
| --- | ---: | ---: | --- | ---: |
| Completion | 43.173 | 0.141 | Index Scan `transponder_completion`, Unique | 19,231 → 4 |
| Departure | 46.071 | 0.132 | Index Only Scan Backward `transponder_facility_start_time`, Limit | 19,305 → 5 |
| Arrival | 47.772 | 0.131 | Index Only Scan Backward `transponder_facility_end_time`, Limit | 19,305 → 5 |
| Evidence | 49.706 | 0.180 | Bitmap Heap Scan + BitmapOr (`transponder_facility_attempt`, `transponder_completion`) | 19,231 → 10 |

All baselines used parallel sequential sample scans. Both latest-start scans had
zero heap fetches after VACUUM. Evidence returned two edges and visited four heap
blocks (two other-facility candidates removed by the final filter). The concurrent
Up took **4.828 seconds** on existing rows. Built index sizes were respectively
47, 74, 74 and 65 MiB (~260 MiB additional; sample heap ~150 MiB). These are single
local runs with mixed cache state, not production latency guarantees. Inserting
into already-built indexes also grows/bloats them; budget more than packed size.

The regression verifies expected index names/no sample Seq Scan, all four catalog
indexes valid/ready/nonunique, concurrent transaction-suppressed Up/Down SQL,
populated Up → Down → Up, one million retained rows, PK-only after Down, and no
pending model changes. Existing PostgreSQL required-point processing/replay and
SQLite domain scenarios provide behavioral compatibility checks.

**Tradeoffs:** four indexes amplify sample insert/update I/O, WAL and disk usage;
no write-throughput/load-concurrency benchmark was performed. Two-dimensional
B-tree overlap conditions do not promise strictly O(window-size) work for every
historical window, extremely long/spanning interval, skewed hot rider, or generic
prepared plan. The OR remains semantically unchanged. Monitor production plans,
ANALYZE/autovacuum and actual distributions; consider a separately reviewed range
index/query redesign only if those workloads justify its extra complexity. A tiny
table may correctly prefer a sequential scan. The tested PostgreSQL version is
17.11; SQLite alone is not evidence of provider plan/concurrency behavior.

## Checks and commands

Run from the assigned worktree. `VELOTIME_TEST_STATISTICS_POSTGRES` must point to
an **isolated disposable PostgreSQL** instance with CREATEDB privileges, never a
shared database. The test creates/drops random child databases.

```sh
# Scaffolding used (already checked in; do not generate a duplicate):
dotnet ef migrations add statistics_sample_attempt_indexes --project src/Module.Statistics --context StatisticsDbContext
# Inspect the additive deployment SQL:
dotnet ef migrations script 20261116120249_statistics_required_course_points 20261116132807_statistics_sample_attempt_indexes --project src/Module.Statistics --context StatisticsDbContext
# Detailed plans, timings, sizes and populated migration round trip:
OIDC_AUDIENCE=https://build.invalid dotnet test src/VeloTime.Workers.Test --filter FullyQualifiedName~SampleIndexesPreserveRowsRoundTripConcurrentlyAndServeAttemptQueries --logger 'console;verbosity=detailed'
# Focused PostgreSQL suite (26 passed, no skips):
OIDC_AUDIENCE=https://build.invalid dotnet test src/VeloTime.Workers.Test --filter FullyQualifiedName~StatisticsValidityPostgreSqlTests --logger trx /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
# SQLite/domain compatibility (54 passed, no skips):
OIDC_AUDIENCE=https://build.invalid dotnet test src/VeloTime.Module.Statistics.Test --logger trx /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
dotnet ef migrations has-pending-model-changes --project src/Module.Statistics --context StatisticsDbContext
dotnet build --configuration Release
```

EF reported no model changes. Full Release build passed (0 errors, 156 existing
warnings, including dependency vulnerability advisories). An initial full build
with `--no-restore` failed on missing unrelated project assets; the normal build
restored them and passed. The PostgreSQL suite initially exposed its hard-coded
migration count/latest-ID assertion; it was updated for migration 13 and reran
successfully. EF tool 10.0.9 emits an older-than-runtime 10.0.12 warning. No shared
database was migrated and no existing migration was rewritten.

Next owner: manually select Tester/Reviewer for independent validation, especially
production-like skew, historical attempts, concurrent writes and cancellation
recovery. No API/messaging/platform implementation dependency is introduced.
