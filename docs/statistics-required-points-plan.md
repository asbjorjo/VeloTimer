# Required intermediate course points for statistics

## Status

Behavior choices confirmed; implementation is in progress.

## Problem and proposed approach

Simple statistics configurations identify start and end course points. A completed sample currently causes StatisticsService to search stored samples for the configured start, then create an entry without checking intervening points. Extend the Simple configuration with zero or more required intermediate course points, selectable in the existing administration UI. The processor checks required observations for the same transponder and relevant start-to-finish attempt and retains the result as invalid if a required point is missing or out of course order. An empty list preserves current behavior. Multi configurations continue to derive from their Simple parent.

## Confirmed behavior

1. All required points must be passed in course order between start and finish. Derive and validate order from the selected layout's ordered segments, including the forward path on closed courses; selecting points must not imply arbitrary user-selected order.
2. Retain an invalid entry if a required point is missing or out of order. Continue to retain duration-invalid entries; apply an explicit deterministic precedence when multiple rules fail.
3. Required points are structural: block edits once results for the configuration or its dependent Multi configurations exist. Existing results remain unchanged.
4. Include required-point selection in the existing statistics administration UI.
5. Do not handle late/out-of-order events retroactively: later arrivals do not repair previously classified entries. Document the limitation.

## Current architecture and design considerations

- `src/Module.Statistics/Service/StatisticsService.cs` selects Simple configurations by end point and chooses a stored `Sample` whose start matches the configured point; it doesn't presently verify an uninterrupted sequence, point order, or matching facility through the entire interval. Avoid accidentally combining observations from separate attempts or facilities; make attempt and boundary semantics explicit and test start=finish, repeated visits, overlapping samples, equal timestamps and transponder isolation.
- `src/Module.Timing/Service/AgentService.cs` generates samples from adjacent passings within an installation. Statistics stores their resolved course-point endpoints and facility in `Sample`; `TimingSampleComplete` is the current processor input. Establish whether existing persisted samples give sufficient, durable evidence for the agreed behavior, particularly for temporal timing-to-course-point associations, gaps, multiple installations and out-of-order delivery. Do not equate unordered point presence with a continuous route without proof. If evidence is insufficient, design the smallest reliable event/storage change, with contract and topology/rollout implications validated first.
- `src/Module.Statistics.Api/Endpoints/StatisticsDefinitionEndpoints.cs` validates point facility membership, guards structural changes after results, and invalidates caches. A new ordered collection requires value-based structural comparison, not reference equality. Validate nonempty/unique IDs, distinctness from endpoints as appropriate, same facility and actual layout/path membership (including loop layouts), with explicit errors; account for referenced point deletion in the existing reference checks.
- Existing configuration records and clients must remain compatible: migrate to an empty required-point set; omitted optional request fields have explicitly defined preserve/clear behavior so older clients and threshold-only updates cannot inadvertently erase requirements. Keep API/BFF/generated client, OpenAPI, cached definition loading, seed paths, and localization consistent. Verify additive migration and downgrade behavior.
- Define exact attempt selection and how to avoid using a checkpoint from a previous lap, a different facility or transponder, including repeated start/end points. Ensure relevant queries are bounded/indexed where justified and preserve current behavior for configurations with no requirements. New Simple entries retain current validity thresholds and Multi contributor logic; no duplicate `EntryCreated`/Multi results on retries.
- `StatisticsValidityEvaluator` stores only the first invalidity reason, while `StatisticsRevalidationService` currently reevaluates Simple entries using duration alone. Persist a separate immutable checkpoint-satisfaction outcome for each new Simple entry (existing entries default to satisfied), independent of the displayed reason, so threshold revalidation cannot accidentally restore a checkpoint-failed entry or lose a concurrent duration failure. Add an explicit checkpoint invalid reason and update the database check constraint, shared evaluation/precedence, revalidation row projection and tests. No late-arrival reassessment. Apply existing Multi invalid-contributor rules and schedule propagation without replaying creation events.
- Since course order is a layout property and endpoints alone do not identify a unique path, contract design must specify how the selected layout and its forward point ordering are represented and validated; handle closed loops, repeated points and ambiguous paths explicitly rather than guessing. Maintain compatibility for existing endpoint-only configurations.
- Time values remain UTC; distances remain SI. Respect service-owned storage and existing message, caching, locking, authorization and transaction conventions.

## Implementation todos

1. Confirm behavior and finalize acceptance criteria: completed.
2. Prepare a dedicated branch and isolated worktree from the agreed up-to-date base; preserve the unrelated pre-existing change to `api/VeloTime.Module.Statistics.Api.json` and all other sessions.
3. Contract-first: assign one specialist to design and validate the actual Simple configuration read/write contract, compatibility for omitted fields, required-point semantics and invalidity precedence, and any necessary messaging contract. Confirm evidence and affected producers/consumers before implementation handoffs.
4. Add durable configuration storage, per-entry checkpoint outcome and migration, including validity constraint/enum; implement domain/API validation, structural guards and point-reference checks; update cache loading/invalidation and client/OpenAPI/BFF mappings without changing unrelated behavior.
5. Implement reliable attempt-scoped intermediate-point verification in processing, using existing persisted samples if proven sufficient; otherwise implement the agreed minimal additional evidence path. Store the checkpoint outcome and classify missing or out-of-order requirements as invalid. Integrate with shared validity evaluation and historical threshold revalidation so checkpoint failure survives threshold changes; propagate to Multi through existing contributor logic. Preserve legacy behavior with no requirements. Handle retries, timestamp boundaries and facility/temporal associations explicitly; do not repair late arrivals.
6. Update the localized statistics configuration UI to select and inspect required points, maintain unsaved state, avoid invalid selections, and follow existing authorization/UX patterns.
7. Add focused domain/API/worker/UI and migration regression tests for the agreed semantics and compatibility. Run targeted test projects and affected Release builds, using PostgreSQL where necessary; verify cache and messaging deployment if touched.
8. Perform bounded correctness review, fix findings, rerun affected checks, update directly related documentation, and make focused verified Conventional Commits with the required coauthor trailer. Coordinator verifies the final diff and reports exact checks/blockers.

Dependencies: confirmed semantics precede contract; contract precedes storage/API and processing/UI; storage precedes persistence-dependent work; implementation precedes regression verification and review. Use sequential specialist handoffs in the same worktree, or separate worktrees and explicit integration for truly independent parallel edits.

## Acceptance criteria

- Configurations without required points retain existing results behavior; existing stored definitions migrate unchanged.
- Multiple configured intermediate points are enforced in course order on the correct rider's specific attempt; prior or different riders' observations cannot satisfy them. Missing or out-of-order points produce retained invalid entries, with no retroactive late-event repair.
- Configuration round-trips via API and administration UI, validates references, respects structural-edit/deletion rules, and remains compatible with existing clients and threshold edits.
- Checkpoint invalidity survives threshold tightening and loosening even when another invalidity reason initially has precedence; dependent Multi entries remain invalid when they include an invalid Simple contributor. No accidental duplicate entries/events. Targeted tests prove the agreed edge cases and verify PostgreSQL persistence/migration where applicable.

## Out of scope unless confirmed otherwise

Historical replay or checkpoint reclassification of existing entries, new rejected-result UI, changes to duration thresholds, manual override, and changes to unrelated timing samples or public result shapes.


## Storage phase handoff (2026-11-16)

Storage is additive and implements the approved contracts from `33725226`; API and
processor behavior is deliberately unchanged in this phase.

### Exact entity fields and caller obligations

- `SimpleStatisticsItemConfig.RequiredCourseLayoutId : Guid?` maps to
  `statistics.statistics_item_config.required_course_layout_id` (`uuid`, nullable).
  It identifies the Facilities layout validated at the structural save, not an EF
  navigation/foreign key or a live instruction to resolve/reorder points.
- `SimpleStatisticsItemConfig.RequiredCoursePointIds : Guid[]` maps to
  `required_course_point_ids` (`uuid[]`, PostgreSQL default `ARRAY[]::uuid[]`). The CLR
  default is empty. EF requires a non-null collection for Simple; the physical TPH
  column is nullable because Multi does not map these properties. Existing Simple
  rows receive an empty array; Multi ignores these columns and inherits its parent.
  SQLite test contexts use EF's built-in primitive-collection JSON representation;
  only PostgreSQL receives the array SQL default. No converter or additional table
  is necessary. Existing indexes are unchanged: processing loads requirements by
  configuration, not by searching inside the array.
- `StatisticsEntry.RequiredCoursePointsSatisfied : bool { get; init; } = true` maps
  to `statistics.statistics_entry.required_course_points_satisfied` (`boolean NOT
  NULL DEFAULT TRUE`). Set it explicitly at Simple entry creation when requirements
  exist. Existing entries, new unconfigured entries and Multi entries default true.
  Multi validity is still determined from contributors, not its own checkpoints.
  The field records the original attempt outcome independently of `InvalidReason`;
  false must survive duration precedence and threshold revalidation. EF rejects
  tracked changes after insertion (`PropertySaveBehavior.Throw`). This is not a
  database trigger: bulk/raw SQL writers must also leave the outcome untouched.

API must copy a validated collection into a new array, compare IDs by sequence and
layout ID by value, and never mutate a saved array in place. Requirements are an
immutable configured snapshot, not a live layout view. Later Facilities layout
changes do not alter stored order. At structural saves, revalidate preserved as
well as replaced references against the proposed endpoints/facility/layout and
block structural changes after Simple/dependent Multi results exist. If current
layout references no longer validate, reject the save rather than silently reorder
the snapshot. Null/omitted replacement preserves both fields on update; explicit
empty replacement clears the array and layout ID. Multi requests cannot configure
these fields. Reference validity, uniqueness, endpoint exclusion, order and
structural-edit guards remain API responsibilities; no cross-service database
access or foreign keys are introduced.

Processor must enforce the stored sequence, persist the original satisfaction
outcome before saving, and use the approved reason precedence (nonpositive,
invalid contributor, checkpoint failure, duration bounds). Revalidation must project
and consume the independent outcome without rewriting it; late arrivals do not
repair classifications. Storage alone does not yet enforce this behavior.

### Migration and deployment

`20261116120249_statistics_required_course_points` lives in actual
`src/Module.Statistics/Migrations` with its generated designer and updated snapshot.
It adds only the three columns and expands `ck_statistics_entry_validity` from
reasons 0–3 to 0–4; existing values/classifications are untouched. Both Up and Down
retain the existing PostgreSQL `NOT VALID` convention: new/updated rows are checked,
no historical entry-table validation scan is added. A future explicit validation
may be scheduled separately. The extension configuration, history conventions and
existing indexes are unchanged; EF history remains `ef.__ef_migrations_history`.
No application row-history triggers/tables exist for these entities to extend.

Apply using the Statistics migration worker before deploying readers/writers of
these fields. PostgreSQL 11+ constant defaults avoid rewriting historical rows,
but `ALTER TABLE` still requires access-exclusive locks; schedule for low traffic
and configure operational lock/timeouts as appropriate. Migration permissions on
the Statistics tables/history schema are required. Only an isolated disposable
PostgreSQL 17 verification database was migrated during implementation.

Down is fail-closed, not a lossy reason remap: within the migration transaction it
locks both tables and refuses when any configured layout/nonempty snapshot,
unsatisfied outcome (even hidden by a duration reason), or reason 4 exists. When
only legacy-compatible data exists it removes the fields and restores reasons 0–3.
Do not use transaction-suppressed execution. Stop feature writers before downgrade;
if blocked, retain the additive schema and roll forward, or obtain explicit approval
for a separately planned archival/data-remediation operation. Do not blindly clear
requirements or convert checkpoint failures to an unrelated reason.

Old code can read/write legacy data after Up because fields have defaults. However,
old processors/revalidation do not honor checkpoint outcomes and old API clients
cannot manage them yet. Do not enable required-point saves or reason-4 processing
until all dependent API/processor/revalidation consumers are upgraded; drain old
writers before activation. No historical replay/backfill is intended.

### Storage checks and Tester scenarios

- Release build of `src/Module.Statistics.Migration` (including domain): passed,
  zero errors. Existing NU1902 WebUtilities advisories remain; checked-in EF tool
  10.0.9 also warns that runtime 10.0.12 is newer.
- `dotnet ef migrations has-pending-model-changes --project src/Module.Statistics
  --configuration Release --no-build`: no pending changes. Forward and reverse
  migration scripts generated successfully and inspected for defaults, unchanged
  history schema, transactional guard and validity values.
- Existing domain tests via `OIDC_AUDIENCE=https://build.invalid dotnet test
  src/VeloTime.Module.Statistics.Test --configuration Release --logger trx
  /p:CollectCoverage=true /p:CoverletOutputFormat=lcov`: 27 passed, none failed/skipped.
- Temporary, uncommitted storage harness run with `dotnet run --project
  /tmp/velotimer-required-points-storage-check --configuration Release`: 13 checks
  passed against disposable PostgreSQL 17 plus SQLite. It migrated from the prior
  schema with legacy Simple/Multi rows and all reasons 0–3; verified defaults and
  preservation, ordered array/layout round-trip, explicit false versus SQL true
  default, reason 4 acceptance/reason 5 rejection, tracked outcome immutability,
  blocked/atomic downgrade for configuration, checkpoint reason and hidden
  checkpoint failure, safe legacy-only downgrade/re-upgrade, and SQLite JSON
  sequence round-trip. Tester should turn these into persistent focused regression
  coverage; SQLite alone does not prove PostgreSQL migration behavior. Also test
  null/omitted versus empty replacement and reference validation in the API phase,
  and creation/revalidation reason precedence and immutable late-arrival behavior
  in the processor phase.
- Existing PostgreSQL validity/coalescing worker regressions via
  `OIDC_AUDIENCE=https://build.invalid VELOTIME_TEST_STATISTICS_POSTGRES=<isolated
  PostgreSQL connection> dotnet test src/VeloTime.Workers.Test --configuration Release
  --filter 'FullyQualifiedName~StatisticsValidityPostgreSqlTests|FullyQualifiedName~StatisticsValidityCoalescingPostgreSqlTests'
  --logger trx /p:CollectCoverage=true /p:CoverletOutputFormat=lcov`: 22 passed,
  none failed/skipped. Initial execution needed `dotnet restore src/Module.Timing.Api`
  for missing transitive assets; then identified the stale 11-migration/latest-ID
  assertion. Only that directly related schema regression was updated to 12/the new
  migration and extended to check array/layout SQL types and the non-null true
  outcome default. Unrelated tests and API/processor production code were untouched.
  Build-regenerated OpenAPI was restored to the worktree's initially clean version.
