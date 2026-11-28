# Timing association persistence checks and deployment

Run the focused checks:

```sh
OIDC_AUDIENCE=https://build.invalid dotnet test src/VeloTime.Module.Facilities.Test/VeloTime.Module.Facilities.Test.csproj --filter FullyQualifiedName~TimingAssociation --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
dotnet build src/Module.Facilities.Migration/VeloTime.Module.Facilities.Migration.csproj --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Module.Facilities/VeloTime.Module.Facilities.csproj
```

Set `FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION` to a **test-only** PostgreSQL control-plane
connection with `Database=postgres` and create/drop database privileges. Each test generates,
migrates and drops its own database. Without configuration, PostgreSQL tests explicitly skip.
SQLite tests use `EnsureCreated`, not PostgreSQL migrations: they prove portable model mappings
and the interval check, not temporal uniqueness or PostgreSQL backfill.

## Migration

`20261102111211_facilities_time_bound_timing_associations` is PostgreSQL-only.
It locks the legacy coursepoint table before checking duplicate non-null timing-point IDs,
and fails with an explicit `23505` error if any ID belongs to multiple coursepoints.
Resolve duplicates through an approved data remediation; the migration never guesses.
Each existing non-null mapping becomes one all-time association (`NULL, NULL`), using the
coursepoint UUID as its initial association UUID. Unmapped coursepoints remain unmapped.
Only after copying and enforcing constraints is `course_point.timing_point` removed.
EF's ordinary transactional migration execution rolls the entire operation back on failure.

`btree_gist` must be available and installed in **public**. The migration creates it if absent;
the migration role needs extension-installation privileges (or an administrator must preinstall it).
An existing installation in another schema must be remediated explicitly before deployment;
`IF NOT EXISTS` does not move an extension. UUID equality uses `public.gist_uuid_ops`.
No UUID-generation extension, cross-service FK, or application-specific history trigger is needed.
The module's existing history-schema migration is empty; EF migration history remains
`ef.__ef_migrations_history`.

Two GiST exclusion constraints prevent intersecting ranges for either timing-point or
coursepoint identity, including concurrent writers. `tstzrange(from, to, '[)')` interprets
null bounds as unbounded and permits touching, non-overlapping endpoints. Checks reject
empty/reversed finite intervals and explicit PostgreSQL timestamp infinities. Non-null CLR
bounds normalize to offset-zero UTC on assignment and EF materialization; PostgreSQL stores
instants at microsecond precision. API comparisons and event snapshots must use persisted precision.
The generated designer/snapshot explicitly retain the public extension schema: the current
Npgsql scaffolder omitted it, causing false pending-model changes until corrected.

Associations restrict coursepoint deletion. Segment-to-point, segment-to-layout and
layout-to-facility FKs also restrict deletion instead of cascading history/geometry away.
An empty, genuinely unreferenced point/layout/facility can still be deleted. Timing-point
existence, same-facility membership and timing-service deletion dependency checks belong to
API/service coordination; Facilities never accesses Timing's database.
Automatic `Down` throws: temporal history cannot safely collapse into the old column.
Rollback requires an approved backup/recovery or forward-fix plan.

Stop old Facilities writers/readers and bootstrap before applying this breaking column-removal
migration. Take a backup, preflight duplicates/extension privileges, and allow for blocking
DDL and index-build time on large tables. Do not deploy this prerequisite alone with old consumers.

## API/messaging integration handoff

`CoursePoint.TimingPoint` is obsolete, unmapped, and throws on get/set. It is a temporary
compile bridge, not a working compatibility layer or second mapping source. Update service
lookup, endpoint projections/writes/deletion guards, bootstrap and legacy test fixtures to
use `TimingAssociations`. Keep the public read DTO unchanged and resolve at one captured
UTC instant (or the supplied instant); unloaded navigation collections are not authoritative reads.
Unlink must retain unaffected history. Mapping-sensitive caches/serialization also need review.

Constraints are `DEFERRABLE INITIALLY IMMEDIATE`. Close/truncate old intervals before inserts,
or defer **both** `facility.ex_timing_association_timing_point` and
`facility.ex_timing_association_course_point` inside the transaction before splitting/replacing.
Deferred conflicts fail at commit. Translate exclusion `23P01`, check `23514` and FK `23503`
errors appropriately, revalidate confirmation under the existing lock, and handle concurrent
writers/deadlocks. Constraints alone do not enforce confirmation, same-facility membership,
or prohibit privileged direct deletion of association history.

`FacilityDbContext` is an ordinary EF `DbContext` and can use
`AddOutboxUsingDbContext<FacilityDbContext>` with the existing SlimMessageBus 3.4.0 package.
The repository's Timing API initializes `smb_outbox` with the bus's own schema initialization,
not EF entity mappings/migrations. Set its PostgreSQL schema to `facility`, provision required
DDL privileges, and fail startup if initialization fails. No outbox table is added by this migration.
This is a baseline capability assessment, **not** a Facilities outbox integration test.

API owner must save association changes and enqueue through the same scoped context/connection
and active transaction, then commit; enqueue, serialization or deferred-constraint failure must
roll back both. Invalidate caches only after commit. Verify this with real PostgreSQL.
Producer must enforce offset-zero UTC `OccurredAtUtc` and before/after bounds, preserve stable
`EventId`/`ChangeId` and payload across delivery retries, and cover displacement/splits/unbounded
windows. Event serialization round-trip and bus/outbox wiring are API/messaging follow-ups.
