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
