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


## Configuration/API transport handoff (2026-11-16)

Implemented after contracts `33725226` and storage `3234d04f`; production processing,
revalidation, Razor UI, shared contracts and storage are unchanged.

- Simple writes resolve omitted/null requirements to the existing snapshot on update
  (none on create). Explicit `{ "courseLayoutId": null, "coursePointIds": [] }`
  clears; a null/missing inner array is rejected. Multi rejects any non-null object,
  including an empty clear object.
- Every Simple save validates the resulting requirements, including preserved ones,
  against proposed endpoints, current Facilities layout geometry and each point's
  facility. The validator sorts **segments** by unique `Order`, verifies continuity
  and unambiguous point occurrence, then validates the supplied snapshot as a strictly
  increasing subsequence of the forward intermediate path. Closed courses permit
  one wrap; equal endpoints mean one full lap. It never sorts/replaces the supplied
  point order to make invalid input succeed. Legacy empty requirements still permit
  endpoint-only configurations without choosing a layout.
- Saved arrays are copied, not mutated. Structural comparisons use layout value and
  point sequence; preserve resolution is local to each transaction attempt so a retry
  reloads the current snapshot instead of overwriting it with a previous attempt.
  Threshold-only and older-client updates do not erase requirements
  or trigger collection-reference false conflicts. Simple/dependent Multi result
  guards remain in the existing locked Statistics transaction. Invalid input returns
  400; valid structural changes with results return 409. As before, validation may
  report 400 before the result guard when a proposed structural change is itself invalid.
- Facilities already exposes `GET /api/facilities/layouts/{id}` with facility and
  ordered segments. The API-local `ICourseLayoutReferences` HTTP adapter consumes
  that existing DTO, forwards caller Authorization, treats 404 as a missing reference,
  and propagates transport/non-success/malformed-response failures rather than accepting
  an empty fallback. No Facilities database access or contract changes were needed.
- Statistics point dependencies now include `RequiredCoursePointIds.Contains(id)`.
  Existing Facilities layout semantic-edit/delete handlers consult dependencies for
  every old layout point, so they also protect required points. Facility deletion is
  already blocked while layouts remain. This is the existing cross-service preflight
  safeguard, not a distributed transaction: concurrent Facilities edits and Statistics
  creates are not globally serialized. The persisted ordered snapshot remains authoritative;
  future Simple saves reject stale/invalid references instead of reinterpreting it.
- Admin and unavailable-public configuration DTO mappings include copied requirements.
  Existing full-entity configuration cache loaders automatically load both scalar/array
  fields, and existing definition tag/key invalidation applies to requirement changes.
  BFF maps generated-client reads/writes, uses a v2 admin configuration cache representation,
  and copies nested arrays on return so consumers cannot poison cached snapshots.
  Browser/BFF pass the additive shared property unchanged. Missing definition-list
  responses now fail explicitly rather than appearing as successful empty lists.
- Statistics OpenAPI is regenerated and checked in. Typed client code is generated in
  `obj` by the existing NSwag build target, not checked in. UTC and SI representations,
  authorization policies and BFF antiforgery behavior are unchanged.

### Exact checks and remaining verification

Commands were run from `/workspaces/VeloTimer-required-points` with
`OIDC_AUDIENCE=https://build.invalid` for builds/tests:

- `dotnet build src/Module.Statistics.Api --configuration Release`: passed, 0 errors.
- `dotnet build src/Module.Statistics.Client --configuration Release`: passed,
  generated the additive NSwag requirements DTO/read/write properties, 0 errors.
- `dotnet test src/Module.Statistics.Api.Test --configuration Release --filter
  FullyQualifiedName~StatisticsEndpointTests --logger trx /p:CollectCoverage=true
  /p:CoverletOutputFormat=lcov`: 26 passed, 0 failed/skipped.
- New focused required-point API tests: 7 passed. The initial test compile exposed
  two init-only fixture assignments; fixtures were corrected, with no production
  behavior workaround.
- `dotnet test src/Module.Statistics.Api.Test --configuration Release --no-restore
  --filter 'FullyQualifiedName~StatisticsEndpointTests|FullyQualifiedName~StatisticsRequiredPointsTests|FullyQualifiedName~PublicStatisticsDiscoveryTests'
  --logger trx /p:CollectCoverage=true /p:CoverletOutputFormat=lcov`: 60 passed,
  0 failed/skipped. These run on SQLite, not PostgreSQL.
- `dotnet build src/WebUI.Mud --configuration Release`: initially blocked by missing
  transitive Profiles/Notifications API assets (NETSDK1004). After `dotnet restore
  src/Module.Profiles.Api` and `dotnet restore src/Module.Notifications.Api`, retry
  passed with 0 errors. Existing NU1902/NU1903 dependency advisories and BL0005 Razor
  warnings remain; API OpenAPI generation also logged existing DataProtection/cache
  configuration diagnostics without failing the build. Unrelated regenerated API
  documents were not retained.
- `dotnet test src/VeloTime.Platform.Test --configuration Release --filter
  'FullyQualifiedName~StatisticsRequiredPointsTransportTests|FullyQualifiedName~StatisticsThresholdTransportTests|FullyQualifiedName~WebUiCacheRegressionTests.StatisticsDefinitions'
  --logger trx /p:CollectCoverage=true /p:CoverletOutputFormat=lcov`: 15 passed,
  0 failed/skipped. Includes 8 new transport cases covering actual generated-client
  JSON, server create/update preserve/replace/clear, ordered read caching/isolation,
  and browser serialization/readback. Existing obsolete API/analyzer warnings remain.
- `git diff --check`: passed. No full solution or PostgreSQL API integration run in
  this phase. No semantic deviations from the approved contracts.

Next owner: Coordinator for sequential processor/revalidation and UI implementation;
Tester for independent coverage and broader validation. Do not activate required-point
saves in production before all processors/revalidation consumers honor the snapshot.
Tester acceptance scenarios: retain these API/transport regressions; exercise real
PostgreSQL array-Contains dependency SQL; Facilities actual edit/delete refusal for
required points; missing/cross-facility point references, reversed open endpoints,
upstream HTTP/auth/malformed failures; Redis cache serialization and cross-process
invalidation; PostgreSQL serialization/deadlock retry preservation with concurrent
  requirement replacement; end-to-end BFF authorization/antiforgery and unavailable discovery
mapping. Processor attempt and threshold-revalidation acceptance criteria above
remain unimplemented by this phase.


## Processing/revalidation handoff (2026-11-16)

Implemented after contracts `33725226`, persistence `3234d04f`, API `4b6f0541` /
`d1a8ffdd`, and BFF transport `6291e60b`. No UI, API, schema, shared contracts,
bus registration, topology, agent headers or installation correlation were changed.

### Evidence and attempt rules

- Only Simple configurations with a nonempty ordered `RequiredCoursePointIds` use
  the new reader. Empty requirements keep legacy selection and contributor behavior.
- `Timing.AgentService` samples adjacent passings within an installation.
  `TimingSampleHandler` resolves **each endpoint at its event instant** using the
  existing Facilities distance API, persists the resolved Sample, then publishes
  SampleComplete. The new reader consumes these persisted facts, not current
  Facilities mappings or a live layout. PostgreSQL message endpoints are normalized
  to persisted microsecond precision before identity matching.
- Recover the completion's facility from its exact persisted edge (transponder,
  both times and both points). Missing evidence, an empty facility or multiple
  matching facilities throws: processing fails/retries rather than acknowledging
  a guessed result. Exact duplicate samples in one facility are equivalent evidence.
- Select the newest observed configured start at or before the completing segment's
  start, considering both arrival and departure observations in that facility for
  that transponder. Never search backwards for a different start whose requirements
  happen to pass. Equal configured start/end means a lap, excluding its final arrival
  from start selection. With no start evidence there is no measurable attempt and no
  entry (legacy no-start behavior); do not invent a start timestamp.
- For that selected interval, require one continuous chain of positive-duration
  edges whose point **and timestamp** endpoints join exactly. Inspect all touching
  edges, including spanning overlaps; ignore only adjacent positive edges strictly
  outside the attempt that agree on its boundary point and collapse identical duplicate edges. Gaps, mismatched joins,
  branches/overlaps, zero/negative intervals and conflicting equal-time observations
  fail closed. Internal starts/finishes separate attempts and cannot be crossed.
  Required points must occur once each, in stored sequence, strictly between start
  and finish; extra non-required intermediate points are allowed. No unordered-presence
  or subsequence search may hide an earlier out-of-order required crossing.
- Persist false even when nonpositive duration takes reason precedence. Shared
  evaluation is nonpositive → invalid contributor → checkpoint failure → duration
  bounds. Revalidation explicitly projects the persisted independent boolean, never
  reruns the chain or changes that outcome. Missing/out-of-order checkpoints retain
  a measured invalid Simple entry rather than discarding it.

The evidence identity is `(transponder, facility, course point, persisted UTC instant)`.
This is sufficient for the requested **course-point observation** ordering, assuming
that identity denotes the same observation regardless of which installation reported
it. Matching a point alone or bridging a time gap is never sufficient. We cannot
certify installation/passing provenance: neither Statistics Sample nor the completion
contract carries installation or passing IDs. Coincident identical observations from
two installations are indistinguishable from duplicate delivery. This implementation
makes no same-installation guarantee. If that stronger guarantee, sub-microsecond
identity, or distinguishing physically separate attempts sharing all boundary facts
is required, Coordinator must first approve an installation/passing/attempt evidence
contract and durable storage rollout; it cannot safely be inferred here. No such
unapproved contract was added.

### Transactions, delivery and late arrival

- Existing owning/dependent item locks, per-transponder ownership transaction,
  cancellation, persisted UTC/SI measurements, cache definitions and live-bound reads
  remain in place. New Simple insertions schedule dependent Multi jobs atomically.
  Multi selection and the exact-count/all-valid fully-contained contributor query
  are unchanged. Multi's own checkpoint outcome remains the default true.
- A required attempt is identified for replay by configuration/transponder/facility/
  persisted finish instant. Existing entries are returned unchanged, not rebuilt
  with newly arrived evidence or a newer start. Their required-parent Multi windows
  likewise reuse the original persisted result for that finish/facility. This relies
  on the existing per-transponder transaction lock across processor replicas; direct
  callers must preserve that lock. Legacy configurations retain their old behavior.
- Processor creation publication currently occurs **after commit, without an outbox**.
  Therefore replay returns the original entry for republication of the same
  EntryCreated payload, rather than suppressing it and losing publication on a failed
  send. Duplicate event deliveries remain possible (at least once); required Simple
  and Multi rows do not duplicate. Exactly-once broker publication is not claimed.
  Existing crash-after-commit/before-publish recovery still requires source redelivery;
  a processor transactional-outbox/inbox change is separate Coordinator/Platform work.
- Sample/message insertion order does not define course order. All evidence already
  persisted when classification reads it can participate, including evidence whose
  own completion message has not yet been consumed. Evidence arriving after that read
  cannot repair the retained outcome, even on duplicate completion or threshold edits.
  No historical repair/replay, waiting window or new continuation event is introduced.

### Checks and remaining owners

Commands executed from `/workspaces/VeloTimer-required-points`:

- `OIDC_AUDIENCE=https://build.invalid dotnet test
  src/VeloTime.Module.Statistics.Test --configuration Release --no-restore --logger trx
  /p:CollectCoverage=true /p:CoverletOutputFormat=lcov`: **54 passed**, zero failed/skipped.
- `OIDC_AUDIENCE=https://build.invalid VELOTIME_TEST_STATISTICS_POSTGRES=<isolated
  localhost PostgreSQL 17 connection> dotnet test src/VeloTime.Workers.Test
  --configuration Release --filter
  'FullyQualifiedName~StatisticsValidityPostgreSqlTests|FullyQualifiedName~StatisticsValidityCoalescingPostgreSqlTests|FullyQualifiedName~TimingSampleEventTimeTests|FullyQualifiedName~StatisticsProcessorRegistrationTests|FullyQualifiedName~WorkerHandlerTests'
  --logger trx /p:CollectCoverage=true /p:CoverletOutputFormat=lcov`: **43 passed**,
  zero failed/skipped. Includes real handler post-commit publication failures followed
  by retry in fresh contexts, late checkpoint insertion, immutable false persistence,
  microsecond boundaries, dependent Multi retry/contributor failure, threshold tightening
  and loosening, and hidden checkpoint failures in the raw-SQL revalidation projection.
  An initial run without the PostgreSQL environment correctly skipped the database
  tests; the subsequent isolated-database runs above executed them, not skips.
- `OIDC_AUDIENCE=https://build.invalid dotnet build src/Module.Statistics.Processor
  --configuration Release --no-restore`: succeeded, **0 errors**, six existing NU1902
  WebUtilities warnings. Worker graph also emits existing migration nullable/NU1903
  warnings. Initial OpenAPI generation logged the existing absent Redis data-protection
  configuration but completed; no generated API changes are included.
- `git diff --check`: clean (only repository line-ending conversion notices).

Next owner: **Tester**, then read-only Reviewer. In addition to the committed cases,
exercise two processor replicas receiving the same required completion concurrently,
rollback/cancellation during evidence reads and ownership assignment, accepted-send /
lost-ack redelivery, full valid closed laps with unselected points and overlapping
installations, temporally reassigned points whose adjacent mappings disagree, and
threshold changes racing creation. Verify original parent payloads/rows are reused,
checkpoint failures never turn valid, exact Multi contributor semantics remain intact,
and no extra ownership or creation side effects escape rollback.

Coordinator should confirm the observation-identity assumption above and the existing
at-least-once publication semantics before activation, coordinate any stronger evidence
or transactional-outbox requirement, and retain the prior migration-first/all-readers-
upgraded rollout gate. Data & Migrations should assess Sample query indexes at expected
volume: latest-start searches deliberately have no arbitrary lookback (long/invalid
attempts must remain measurable), while chain materialization is bounded by the chosen
attempt interval. No schema/index change was made in this processing phase.


## Blazor UI handoff (2026-11-16)

Implemented the localized Simple configuration selector using the existing shared
`StatisticsRequiredCoursePoints` and browser/BFF services. No API, processor,
storage, shared-contract or package changes are included.

- A checkbox group lists only the selected layout's intermediate points on its
  continuous, unambiguous forward path, ordered by segment `Order`. Closed paths
  wrap once; equal endpoints mean one full lap. Endpoint/off-path selections and
  ambiguous/disconnected geometry are not offered. Selection click order never
  determines the submitted order.
- Saved requirements select their exact saved layout, not the first layout sharing
  endpoints. Only legacy definitions without requirements infer a layout on edit.
  The editor and configuration list display required points; Multi explains parent
  inheritance and sends no requirements.
- Drafts copy the saved snapshot. Unchanged requirements send null (preserve),
  including threshold-only saves after a layout detail failure. Explicit clearing
  sends an empty array with null layout ID. Changed selections/layouts send a new
  ordered replacement. Endpoint/layout changes retain existing selections and show
  validation instead of dropping or silently reordering the snapshot. Invalid
  structural saves require restoring the path or explicitly clearing/reselecting.
- Loading, empty paths/layouts, invalid paths and reference failures have localized
  states. Loading/failure never clears the selected endpoints or required points.
  Failures offer retry and retain HTTP authorization feedback. Reference generations
  ignore abandoned or disposed-editor responses; the existing lifetime cancellation
  and unsaved-changes/navigation tracking remain in use. A 409 explains the
  server-authoritative result-dependent structural lock and retains the draft.
- English and Norwegian Bokmål labels/help/errors cover the fieldset/legend,
  native labelled checkboxes, selected-point list, explicit clear action and polite
  loading/validation announcements. Existing responsive grid/unit presentation stays
  unchanged. No UTC or SI conversion was added.

Checks from `/workspaces/VeloTimer-required-points` with
`OIDC_AUDIENCE=https://build.invalid`:

- `dotnet build src/WebUI.Mud.Client --configuration Release --no-restore
  --verbosity quiet`: passed, 0 errors (existing NU1902/NU1903 and BL0005 warnings).
- `dotnet test src/VeloTime.Platform.Test --configuration Release --no-restore
  --filter 'FullyQualifiedName~StatisticsRequiredPoints|FullyQualifiedName~StatisticsThreshold|FullyQualifiedName~LocalizationContractTests.Resources_ProvideCompleteNorwegianTranslationsAndMatchingPlaceholders|FullyQualifiedName~WebUiCacheRegressionTests.StatisticsDefinitions'
  --logger trx /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
  --verbosity quiet -clp:ErrorsOnly`: 66 passed, 0 failed/skipped. Includes 27 new
  draft/actual Razor editor regressions for open/closed/full-lap order, bad geometry,
  reverse selection order, explicit clear, saved-layout choice, threshold preservation
  during reference failure, changed endpoints/layout, Multi inheritance, 401/403/409,
  unsaved state, stale requests and en/nb-NO accessible rendering. Initial test-only
  compile errors were corrected; rendering tests also caught and fixed a string
  parameter binding that would have hidden the selector behind a spurious warning.
- `dotnet build src/WebUI.Mud --configuration Release --no-restore
  --verbosity quiet -clp:ErrorsOnly`: passed, 13 existing warnings, 0 errors.
- `git diff --check`: passed. Build-generated unrelated OpenAPI was not retained.

Visual validation: no browser automation tool was available. Static HtmlRenderer
checks validate markup and component/service interactions, not browser layout,
keyboard operation, screen-reader behavior or real authentication/antiforgery.
No full-solution, database or live end-to-end UI test was run in this phase.

Next owner: **Tester**, then read-only Reviewer. Manually exercise keyboard selection
and clear/cancel/navigation prompts, small-screen wrapping with long point names,
both languages, a wrapped/full-lap layout, changed/missing layouts, slow/failing retry,
and saved requirements shown in the list. Verify threshold-only edits with recorded
results succeed while valid requirement changes/clears return 409 and retain the
draft. Exercise real BFF authorization/antiforgery and reload round-trips. Coordinator
retains the migration-first/all-readers-upgraded rollout gate and prior processing
observation-identity/at-least-once publication decisions.
