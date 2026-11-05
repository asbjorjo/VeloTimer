# Hall of Fame implementation plan

Status: Superseded by the basic fixed-tab Hall of Fame implementation. This document
is retained as historical design context, not the current feature specification.
See the [current user documentation](../README.md#hall-of-fame) and
[API contract](../api/statistics-public-contract.md#additive-hall-of-fame-contract).
The implemented scope has six fixed tabs, an optional facility filter, no admin
editor, and lap totals using the current Lap distance with no facility-name column.

## Problem and approach

Add a dedicated Hall of Fame page showing overall top rankings for selected statistics items. Statistics administrators select sections, their order, ranking mode, and visible table values. Persist settings in the Statistics database and add dedicated public/admin contracts without changing existing results endpoints.

## Repository findings

- StatisticsItem has name, description and distance. Simple/Multi configurations belong to items.
- StatisticsEntry holds duration, speed, completion time, facility, configuration, transponder and optional linked profile. StatsProfile is keyed by UserId and controls public visibility.
- BestStatisticsResultsEndpoints returns only one fastest public entry per item across configurations, not top-N riders. Preserve its contract and home-page consumers.
- PublicStatisticsEndpoints supports anonymous chronological results and current public visibility, not leaderboard aggregation.
- StatisticsDefinitionEndpoints and StatisticsDefinitions.razor enforce existing statistics-admin policies. Extend those authorization patterns through API, BFF and UI.
- Both browser/server transports exist; Statistics clients use the existing OpenAPI/NSwag build pipeline.
- API PostgreSQL tests and platform transport/BFF/browser tests provide prior art. UI uses shared presentation helpers and English/Norwegian resources.

## Confirmed requirements

1. Anonymous public page; all-time rankings across all facilities and configurations belonging to each selected item, with one row per public rider/profile. No date/facility/category filters in this release.
2. Passing count means completed StatisticsEntry results for that item, aggregated across the rider's linked transponders. Do not count raw timing-point passings.
3. Best time ranks each rider's shortest duration ascending; Most passings ranks completed-result counts descending. The same item may appear in separate sections using both modes.
4. Default 10 rows per section; administrators may choose 1-100. Ties never expand the table. Equal primary values share competition rank (1, 1, 3); deterministic secondary ordering selects the rows within the configured limit. Define and validate exact secondary ordering during the contract-first phase.
5. Rank and rider name remain fixed. Administrators select and order optional Best time, Speed of best result, Passing count, Facility of best result and Date of best result columns per section. All best-result values come from the same deterministically selected fastest result, including in passing-count sections.

## Todos

1. Requirements and acceptance criteria finalized from user confirmation; this planning prerequisite is complete. Await explicit implementation authorization before step 2.
2. Create an isolated worktree and dedicated branch from the agreed up-to-date base. Preserve other worktrees and user changes.
3. Contract-first (.NET API Developer): define additive persisted-section settings/save contracts, ranking-mode/column enums, public leaderboard DTOs and shared service signatures. Specify limits, ordering, tie rules, optional column payloads and concurrent-admin save semantics. Validate actual contracts before dependent implementation. Exclude internal user/transponder identifiers from public DTOs. New backend quantities are base SI and times UTC; do not change legacy km/h contracts.
4. Persistence (Data & Migrations Developer): store ordered sections, item references, ranking modes, enabled state, row limit and ordered columns in the Statistics service. Use the simplest compatible schema, migration/snapshot and clear item-deletion rules. Save/reorder atomically. Add indexes based on actual ranking SQL and PostgreSQL evidence, not assumption that chronological indexes suffice.
5. API and transports (.NET API Developer): implement anonymous Hall of Fame reads and statistics-admin settings reads/saves. Validate unsupported values, references, column combinations and bounds; surface errors explicitly. Compute per-profile rankings and top-N in SQL with deterministic ordering and current visibility checks. Avoid per-rider dependency lookups. Wire route registration, BFF, both service transports and generated client/OpenAPI. Honor optional-column selections at the public payload boundary. Resolve facility names through existing HTTP clients, never another service's database. Prevent unnoticed lost admin updates.
6. UI (Blazor UI Developer): add /hall-of-fame and public navigation. Render sections and columns in persisted order with localized headings and shared time/unit formatting; support responsive tables, loading, errors/retry and explicit empty states. Add statistics-admin editor for selected sections, both ranking modes, accessible reorder controls, column selection/order, row limits and atomic save with unsaved-change handling. No automatic sections when settings are empty. Update English/Norwegian resources and admin navigation.
7. Verification (Tester): cover persisted section/column order, both modes for the same item, exact row limits, ties, multiple transponders per rider, duplicate profile names, consistent best-result columns, public visibility changes, unlinked/private exclusions, authorization, validation, concurrency, dependency failures and browser/server compatibility. Exercise real PostgreSQL aggregation/translation and query plans. Run smallest affected Statistics/API/platform suites using run-tests skill, OIDC_AUDIENCE=https://build.invalid and required PostgreSQL; run relevant Release builds. Report unavailable/unrun checks explicitly.
8. Review and completion (Code Reviewer, read-only): review substantial changes and regression evidence; send fixes to original owners and rerun affected checks. Update directly related API and user documentation. Inspect final diff, migration and generated artifacts. Make focused verified Conventional Commits with required co-author trailer as work progresses.

Dependencies: decisions -> worktree -> contracts -> persistence -> API/transports -> UI -> tests -> review/final acceptance. Prefer sequential specialist handoffs for this tightly coupled feature. Parallel editing would require distinct worktrees and explicit integration. This document records the approved requirements, not authorization to implement. Implement only after the user explicitly requests it.

## Acceptance criteria

- Public visitors see exactly the configured sections and values in saved order.
- Only statistics administrators may modify settings, enforced through API/BFF/UI.
- Rankings are all-time across all facilities/configurations for each selected item, grouped by public rider/profile; count only completed statistics results across linked transponders.
- Both ranking modes may coexist for the same item. Each section defaults to 10 rows, supports limits from 1-100, and never expands for ties; ranking and cutoff order are deterministic.
- Rank/name are fixed; optional approved columns follow saved selection/order and refer to the same best result where applicable. No private or unlinked riders or internal identifiers are exposed.
- Settings survive reload/restart; empty configuration, empty results and failures have distinct localized presentation.
- Existing chronological statistics and home-page best-results behavior remain unchanged.
- New backend data uses UTC/base SI; UI follows locale and preferences.
- Required targeted tests and builds pass, including PostgreSQL verification of actual ranking queries.

## Considerations

Cross-configuration aggregation is explicitly confirmed and mirrors existing best-results semantics, even when configurations describe different courses. Completed statistic entries, not raw passings, are explicitly confirmed as the count source. Avoid caching public visibility in a way that leaks newly private profiles. Initial inspection used the current workspace read-only; repository implementation must use a separate worktree.
