# Statistics item validity thresholds

## Status and approach

Planning complete; implement only when explicitly requested. No repository files changed.

Add optional global approval thresholds per statistics item and optional thresholds on existing facility-linked configurations. Statistics admins can enter speed or duration; persist/exchange only nullable minimum/maximum duration. Retain invalid entries with a validity flag/reason, and filter them out of statistical results.

Save threshold updates promptly and trigger durable, asynchronous historical revalidation through a small integration event. Millions of entries must not be recalculated inside the update request. Eventual consistency is accepted: existing stored validity remains authoritative until background processing updates it. No revision-gated public reads, atomic cutover, or temporary blanket exclusion.

## Confirmed behavior

- Historical entries are revalidated after threshold changes, including loosening that can restore validity.
- Unset configuration bounds inherit global bounds. Global bounds are hard limits: configurations may narrow but never broaden the effective approved range.
- Allow saving broader requested local values, but warn that global limits take precedence and show the actual effective bounds.
- Every contributing lap must be valid for a Multi result to be valid, and its total must satisfy the Multi item's own effective bounds. Never skip invalid laps to assemble a valid result.
- No rejected-results inspection screen or manual per-entry override.
- Thresholds are optional; unset global bounds are unbounded. Boundaries are inclusive. No arbitrary default speeds.
- During background revalidation, old classifications and temporarily stale Multi results may remain visible. This is explicitly accepted eventual consistency, not an error or a reason to hide all affected results.

## Duration and warning semantics

- Add nullable MinDuration/MaxDuration to StatisticsItem, and nullable local duration bounds to the StatisticsItemConfig base shared by Simple/Multi. Extend read/save contracts compatibly for callers omitting the fields.
- Effective minimum duration = maximum of the non-null global/local minima. Effective maximum duration = minimum of the non-null global/local maxima. If neither exists on a side, it is unbounded.
- Minimum speed maps to maximum duration: MaxDuration = distance / MinSpeed. Maximum speed maps to minimum duration: MinDuration = distance / MaxSpeed.
- Warn when requested local minimum speed is below global minimum speed or requested local maximum speed exceeds global maximum speed. Equivalent duration warnings: local MaxDuration above global MaxDuration or local MinDuration below global MinDuration. Explain clamping; do not imply the broader requested range is approved.
- Validate positive finite entered values, overflow, and effective min <= max. Global edits must also check affected effective configuration ranges; reject disjoint ranges explicitly.
- UI offers speed/duration input selection, read-only equivalent values and inherited/effective previews. Reuse locale and preferred km/h, mph or m/s conversions. Preserve exact canonical durations on untouched saves/mode changes and test rounding at persistence-supported precision.
- Configuration facility is derived from Simple coursepoints or the Multi parent; do not introduce redundant facility ownership or cross-service database access. Multi uses its own item's distance/global thresholds.
- Persist IsValid and a small invalidity reason, retaining original measurement times/duration and ownership. Nonpositive elapsed time is invalid even without bounds; define an explicit handling/representation for nonfinite speed during implementation rather than inventing a valid measurement.
- Permit threshold-only edits with recorded results. Preserve existing structural distance/type/coursepoint/item/parent/repetition edit protections.

## Background revalidation

1. Save definitions and a durable event/outbox request together using the existing repository SlimMessageBus EF outbox pattern after inspecting Facilities/Timing prior art. No unprotected save-then-publish. Event identifies the affected item/configuration, not millions of entry IDs.
2. Processor revalidates affected Simple entries and then affected Multi entries in bounded, resumable batches. Use indexed keyset progression, not OFFSET/whole-table loading, and short transactions; use set-based SQL when straightforward. Keep processing within Statistics-owned storage.
3. Reuse durable messaging retries/continuations and checkpoint only as needed to avoid rescanning millions of entries after failures. Do not create a separate orchestration service or elaborate job framework. Keep each message's work bounded rather than holding a broker lock for an entire millions-row scan.
4. Read current thresholds from storage for each batch. Duplicate/out-of-order events must be harmless and newer changes must eventually win. Ensure a concurrent stale batch cannot become the final classification after a newer update; use simple per-scope ordering or a lightweight generation check only where needed for worker writes, not public-query revision gates.
5. New entries use current effective bounds. Existing cache invalidation remains wired across API/processors/WebUI. Account for late concurrent inserts with an eventual follow-up sweep where necessary; do not claim a snapshot cursor alone covers every racing insert.
6. Recheck dependent Multi results after parent threshold changes even when the Multi item's own rules did not change. Preserve fixed constituent laps rather than filtering invalid parents out of aggregation windows. Reclassification updates validity only; it must not replay creation events and duplicate aggregates.
7. Determine contributors from actual aggregation behavior. New lineage fields are needed only if exact contributors cannot be reliably recovered. Existing rows have no explicit lineage: do not silently substitute laps or declare uncertain legacy Multi rows valid. Report a concrete unrecoverable case before deciding any remediation outside this scope.
8. Errors remain explicit in logs/retries; saving settings must not claim historical recalculation has completed. A simple pending notice in the existing editor is sufficient; no inspection screen or new monitoring dashboard required.

## Examined surfaces

- src/Module.Statistics/Model/StatisticsItem.cs, SimpleStatisticsItemConfig.cs and StatisticsEntry.cs: definitions, configuration types and retained results.
- src/Module.Statistics/Storage/StatisticsDbContext.cs and src/Module.Statistics/Migrations/: actual EF schema/migration location (runner is separate).
- src/Module.Statistics/Service/StatisticsService.cs: Simple creation and Multi aggregation. Any selection changes needed to identify exact contributors must be scoped to this feature, not unrelated processing cleanup.
- src/Module.Statistics/Service/StatisticsCache.cs: embedded item/configuration definitions and existing invalidation/backplane helpers.
- src/Module.Statistics.Api/Endpoints/StatisticsDefinitionEndpoints.cs: admin CRUD, policy, validation and recorded-result guard comparing complete configuration DTOs.
- src/Module.Statistics.Api/Endpoints/PublicStatisticsEndpoints.cs and BestStatisticsResultsEndpoints.cs: apply stored IsValid filtering before paging, lookahead, cursor existence, grouping and winner selection.
- src/Module.Statistics.Processor/Handlers/SampleCompleteHandler.cs and EntryCreatedHandler.cs: creation event behavior must retain invalid laps/Multi records and avoid duplicate aggregates on revalidation.
- src/Module.Statistics.Processor/Service/StatisticsOwnershipService.cs: transactional creation/advisory locks; retained invalid entries still require ownership reconciliation.
- src/Module.Statistics.Interface/Data/StatisticsDefinitionDTO.cs and Messages/SampleComplete.cs: shared definition DTOs and existing EntryCreated/SampleComplete contracts.
- src/Module.Statistics.Client/VeloTime.Module.Statistics.Client.csproj: NSwag build-generated client; refresh checked-in OpenAPI rather than manually editing generated code.
- src/WebUI.Mud/Services/StatisticsAdministrationService.cs and Endpoints/StatisticsAdministrationEndpoints.cs; browser administration service/interface: transport mapping, policy and caches.
- src/WebUI.Mud.Client/Pages/Admin/StatisticsDefinitions.razor and Services/EffectiveDisplaySettings.cs: existing editor, inheritance/facility display and locale/unit helpers.
- Statistics API currently has no message-bus registration; explicitly wire producer/outbox hosting, processor subscription and necessary Aspire/Helm configuration using repository patterns.

## Implementation todos and owners

1. **Confirming behavior:** completed. Eventual consistency and the four requested policies are agreed.
2. **Preparing isolated worktree:** after implementation is requested, create a dedicated implementation branch/worktree from the agreed up-to-date v2 base; preserve other sessions/user work. Planning itself needs no worktree.
3. **Defining shared contracts:** .NET API Developer owns contract-first duration DTO fields, invalidity type/serialization, optional-field compatibility and the small threshold-change event. Confirm semantics and validate actual artifacts before dependent implementations. Coordinate event semantics with Messaging specialist; no independent contract redefinition.
4. **Adding storage/migrations:** Data & Migrations Developer adds bounds/validity/reason, migration/snapshot, and necessary outbox/checkpoint storage using existing patterns. Default existing rows compatibly, then use asynchronous revalidation rather than a migration-long historical scan. Add constraints/indexes only when justified. Agree explicit shared entity ownership.
5. **Implementing administration/read APIs:** .NET API Developer adds validation, threshold-only edits, preserved structural guards, statistics-admin authorization, transactional outbox request, stored-validity SQL filters, generated OpenAPI/client and server/browser mappings/cache invalidation.
6. **Implementing processing/revalidation:** Messaging & Workers Developer owns shared evaluation (coordinate ownership with API), creation validity, exact Multi contributor checks and bounded durable historical revalidation. Use current definitions, retry safely and ensure final convergence after superseding edits. Preserve ownership handling and avoid creating duplicate aggregates.
7. **Implementing administration UI:** Blazor UI Developer adds optional bounds, input mode, effective/inherited previews, localized clamping warnings, reset/validation/unsaved-change behavior and locale/unit-safe conversion. No inspection screen. Saving remains prompt with a simple asynchronous-recalculation notice.
8. **Wiring messaging deployment:** Platform & Deployment Developer adds only necessary Statistics producer/outbox and consumer registration/infrastructure wiring. Verify threshold messages do not reach incompatible existing consumers/session filters. Use existing topic/hosting conventions.
9. **Adding/running regressions:** Tester adds targeted domain/API/worker/UI-transport and deployment coverage, runs affected tests and Release builds, reports exact results and returns production defects to original owners.
10. **Reviewing/delivering:** Code Reviewer performs bounded read-only correctness review after implementation/test evidence. Owners fix actionable issues and rerun affected checks. Coordinator verifies integration/diff/acceptance criteria. Update directly related documentation/localization/OpenAPI/changelog where applicable and make focused verified Conventional Commits with required Copilot coauthor trailer.

Contracts precede storage/services/UI/deployment. Storage precedes dependent behavior. Prefer sequential specialist handoffs in one worktree; genuinely parallel editors need separate worktrees/branches and explicit integration. Specialists must not delegate further. No production edits during planning.

## Acceptance and validation

- Only statistics admins write thresholds through API/BFF; omitted optional bounds preserve positive-duration behavior.
- Global/local intersection, null inheritance/clearing, one-sided bounds and disjoint effective-range validation work for Simple/Multi. Broader local values warn but never weaken global limits.
- Inclusive boundaries pass; immediately outside entries remain stored with invalid reason. Speed/duration inversion and locale/unit conversion preserve canonical values.
- All contributors must be valid for Multi validity, as well as its own total range; rejected laps are never skipped to bridge a valid sequence.
- Public paging/lookahead/cursor existence/best winners exclude stored invalid entries before limiting/grouping. Reference/deletion counts and ownership reconciliation include retained invalid rows.
- Tightening/loosening eventually reclassifies historical Simple and dependent Multi rows correctly. Existing results remain visible under their stored classification until processed; no revision visibility gate.
- Update request performs definition/outbox work only, not an entries scan. Verify bounded memory and indexed batch queries on representative high-volume PostgreSQL data without promising an unmeasured timing threshold.
- Test retry/crash/resume, duplicate/out-of-order events, superseding edits, late creation, dependency propagation, durable publish failures, explicit errors and no aggregate duplication.
- Verify migration/backfill compatibility, generated transports, authorization, caches and messaging deployment. Preserve structural edit guards.
- Start with affected Statistics domain/API, relevant Workers and Platform tests; use OIDC_AUDIENCE=https://build.invalid where needed. Inspect runner settings for exact commands. Build affected projects in Release; escalate only as necessary. Planning-only artifacts need no tests.

## Exclusions

No per-rider thresholds, manual validity override, rejected-entry deletion, inferred/default speed limits, raw Timing sample changes, public speed-unit/paging redesign, inspection screen, atomic historical cutover, query-time revision gates, or unrelated cleanup.
