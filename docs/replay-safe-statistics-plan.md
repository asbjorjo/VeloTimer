# Replay-safe, order-independent statistics: refactoring plan

Status: draft for review. Planning only; no code changed.
Base: `v2` at `53ba7b9a`. Builds on, and partly supersedes, [PR #132](https://github.com/asbjorjo/VeloTimer/pull/132) and [`docs/research/agent-timing-statistics-messaging-plan.md`](./research/agent-timing-statistics-messaging-plan.md).

## 1. Requirements

1. **Out-of-order events are mandatory.** A passing may arrive after later passings for the same rider. Results must equal what in-order arrival would have produced.
2. **Replays are safe.** Agent replays are rare. Statistics replays happen when layouts or timing-point associations change. Neither may create duplicates.
3. **All configuration that affects a result is time-bound**: timing-point ↔ course-point associations and transponder ownership (already done), plus facility layouts, segment lengths/order, required course points and layout on statistics configs, installation ↔ facility, and the other items listed in §4.
4. **Config changes trigger recompute**, and each recompute uses the config that was active at the time of the events it uses.
5. **Alpha policy**: breaking changes are fine. Deleting derived data is preferred over migration. No backward compatibility, no legacy-message aliasing, no mixed-version rollouts.

## 2. What happens today (v2)

```
Agent ──PassingEvent──▶ Timing ──TimingSampleComplete──▶ Statistics ─┐
 (no passing ID)        derives sample   (no ID)        inserts Sample │ SampleComplete / EntryCreated
                        from "previous"                  calls Facilities│ over the bus back to itself
                        passing                          over HTTP       ▼
                                                         Simple entries → Multi entries
```

| # | Problem | Where |
|---|---|---|
| T1 | `PassingEvent` has no stable ID. The agent's local passing ID is dropped by the mapper. | `Agent.Interface/Messages/Events.cs`, `Agent/Mapping/EventMappers.cs` |
| T2 | A late passing P2 between P1 and P3 throws "Sample found with mismatched passings" on every retry. It can never be processed. | `Module.Timing/Service/AgentService.cs` `RegisterSampleAsync` |
| T3 | Timing saves the passing, saves the sample, and then publishes, all as separate steps. A crash between them loses or repeats messages. `TimingSampleComplete` has no ID. | `PassingObservedHandler.cs` |
| T4 | The "previous passing" comes from a cache keyed on the last passing, so the result depends on arrival order. | `AgentService.GetPreviousPassing` |
| S1 | `TimingSampleHandler` inserts a `Sample` with no uniqueness check, so redelivery duplicates it. | `TimingSampleHandler.cs` |
| S2 | Simple, Multi and required-point entries are built incrementally from whatever has already arrived. A late sample does not rebuild laps or Multi windows that ended after it. Required-point results are deliberately frozen. | `StatisticsService.ProcessCompletedAsync` / `ProcessEntryAsync`, `RequiredCoursePointAttemptReader` |
| S3 | Statistics enriches each sample with a synchronous Facilities HTTP call made at processing time, so a replay can see different Facilities data. | `Distance2Async` |
| S4 | Statistics talks to itself over the bus (`SampleComplete` → `EntryCreated` → Multi). This adds ordering and duplicate hazards for no benefit. | handlers in `Module.Statistics.Processor` |
| C1 | `CourseLayout` and its segments (order, length) are edited in place. Distance resolution needs exactly one layout containing both points, with no notion of time. | `Facilities/Model/CourseLayout.cs`, `FacilitiesService.CalculateCoursePointDistance` |
| C2 | `Installation.Facility` is a mutable field. | `Timing/Model/Installation.cs` |
| C3 | Statistics definitions (`RequiredCoursePointIds`, `RequiredCourseLayoutId`, item `Distance`/`DistanceMode`, Multi `Repetitions`, duration thresholds) are mutable. Required points are edit-blocked once results exist. Thresholds revalidate history using *current* values. | `Statistics/Model/SimpleStatisticsItemConfig.cs`, `StatisticsItem.cs` |
| C4 | `CoursePointTimingAssociationsChangedV1` is published with an outbox, but nothing consumes it. Stored statistics go stale after a backdated edit. | `Facilities.Api/TimingAssociationEventPublisher.cs` |
| ✓ | Transponder ownership is time-bound, and Statistics already reconciles entries on `TransponderOwnershipChanged`. Keep this. | `StatisticsOwnershipService.ReconcileAsync` |

### How far PR #132 gets

#132 (open, about 11.7k lines) fixes S1 and the duplicate half of S4. It adds a sample natural key (transponder, timing points, times), PostgreSQL outboxes for `SampleComplete`/`EntryCreated`, per-transponder receipts and head release, and Multi creation decisions. It does **not** address T1–T4 (Timing still throws on late passings), S2 (late data still yields wrong laps or Multi windows), S3, or any of C1–C4. Most of its code exists to keep the incremental self-messaging chain (S4) ordered and idempotent, and to support careful migrations and legacy-message compatibility. This plan removes that chain, and the alpha policy removes the need for the migration and compatibility work.

**Recommendation:** do not merge #132. Close it once this plan's Statistics phase lands, and reuse its useful pieces: `StatisticsStoragePrecision` timestamp normalisation, the PostgreSQL test fixtures, and the duplicate, concurrency and rollback test scenarios.

### How far the existing messaging plan gets

That plan is a sound process framework: stable IDs, inbox/outbox, a split control topic, and an operator UI. But it leaves the core design questions open ("replay-storage architecture", "late-data policy", "event-time config"). This plan answers them (§3). Its confirmed decisions still hold, except where the alpha policy now makes them simpler. In particular, "manual invalidations are flagged for review, not dropped" carries over (§5.4).

## 3. Target design

**Statistics becomes a deterministic, recomputable projection.** It is a pure function of (a) the passing facts Timing has recorded and (b) the time-bound configuration history. Every way results can change goes through **one mechanism: mark a time range dirty, then recompute it.** This covers a live passing, a late passing, an agent replay, a config edit, and a code fix.

```
Agent ─PassingObservedV1(id)─▶ Timing ─PassingRecordedV1(id)──────────────▶ Statistics
                               store fact idempotently,                     store fact idempotently
                               outbox in same tx                            mark session dirty
Facilities ─CourseConfigChangedV1(interval)──▶ Statistics: config replica + mark range dirty
Timing ─InstallationFacilityChangedV1 / TransponderOwnershipChanged──▶ same
Statistics API (definition edits) ──────────▶ same, in-process
                                              │
                         Recompute worker: per (transponder, facility, session)
                         rebuild samples → Simple → Multi in event-time order,
                         using config valid at each event's time, in one transaction
```

### 3.1 Facts, not derived samples, cross service boundaries

- The agent sends `PassingObservedV1 { SourcePassingId, AgentId, Time, TransponderType, TransponderId, LoopId, flags }`. `SourcePassingId` is the agent's existing local passing ID. Timing deduplicates on `(AgentId, SourcePassingId)`. If the same ID arrives with a different payload, Timing rejects it and records a conflict for review (an existing confirmed decision).
- Timing persists the `Passing` and an outbox row `PassingRecordedV1 { PassingId, InstallationId, TimingPointId, TransponderId, Time, flags }` in **one transaction**, using SlimMessageBus's PostgreSQL outbox, which Timing.Api already uses for ownership.
- **Timing stops deriving samples for Statistics.** `TimingSampleComplete` is deleted. Timing's own `Sample` table and the "previous passing" cache are removed. `SampleEndpoint` computes adjacent pairs on read with a window function (`LAG() OVER (PARTITION BY installation, transponder ORDER BY time, id)`). This removes T2, T3 and T4 outright, because there is nothing left to get out of order.
- Statistics stores its own `PassingFact` copy, idempotent on `PassingId` (unique key). A duplicate delivery inserts nothing and only re-marks the same range dirty, which is harmless.

### 3.2 Recompute unit: the riding session

A **session** is a maximal run of one transponder's passings at one facility in which consecutive passings are less than `SessionGap` apart. `SessionGap` is configurable, defaulting to 30 minutes. Sessions are small (tens to a few hundred passings), and no lap, required-point attempt or Multi window spans one. So "recompute the session(s) overlapping [t₀, t₁)" is cheap, and it is always correct.

- In-order live passing: only the tail of the current session changes. The worker recomputes from the previous passing onward, which keeps latency low.
- Late passing: it may fall inside a session, extend one, or join two sessions. The worker recomputes the merged session.
- Config change over [from, to): the worker recomputes every session overlapping the interval for the affected facility, timing points or item.

**Decision:** Multi windows are confined to one session. Today they can chain across arbitrary gaps.

### 3.3 Dirty ranges and the recompute worker

- A `RecomputeRequest(Scope, From, To, Reason, CausationId)` row is inserted in the same transaction as the fact or config replica change. Scope is a transponder plus facility, or a facility, or an item. The worker reuses the durable checkpoint pattern of `StatisticsValidityRevalidation`.
- The worker coalesces overlapping requests, expands each one to whole sessions, and takes the per-transponder advisory lock (the existing `StatisticsOwnershipService` pattern). In one transaction it then:
  1. loads the session's `PassingFact`s ordered by `(Time, PassingId)`, a deterministic tie-breaker (see §6);
  2. resolves each passing's timing point → course point at its **own** time, and each sample's layout/segment distance at its time, from the local config replica (§4.3);
  3. derives samples, then Simple entries per active config version, then Multi entries, then validity, then ownership;
  4. **diffs** the result against stored rows by deterministic identity (§3.4): unchanged rows are left alone, changed rows are updated, missing rows are deleted, and new rows are inserted. It also enqueues any outgoing integration events into the outbox, which is only needed if something external consumes results.
- Bulk config-change recompute runs in the background with throttling and progress. Readers see each session switch atomically from old to new (eventual consistency, the same as the accepted threshold-revalidation behaviour).
- Remove `SampleComplete`, `EntryCreated`, their handlers, subscriptions and outboxes (S4). Statistics no longer messages itself.

### 3.4 Deterministic identities

Derived rows get IDs computed from their sources (UUIDv5 over a versioned natural key), so a recompute produces the same ID for the same result:

- Sample: `(StartPassingId, EndPassingId)`.
- Simple entry: `(ConfigVersionId, StartPassingId, EndPassingId)`.
- Multi entry: `(MultiConfigVersionId, first contributor EntryId, last contributor EntryId)`.

The same key on replay makes duplicates impossible by construction: the key is unique in the database, and the diff step is the only writer.

### 3.5 Late-event horizon

The existing plan confirmed a 24-hour automatic-correction horizon. With session recompute, automatic correction is cheap, so this plan keeps the horizon only as a guard: a passing more than `AutoCorrectHorizon` late is stored, but it is flagged for review instead of auto-recomputed. Config-driven recomputes ignore the horizon, because they are deliberate admin actions.

## 4. Time-bound configuration

### 4.1 One pattern everywhere

Every result-affecting config item gets **effective-dated versions** over a half-open interval `[ValidFromUtc, ValidToUtc)`. Null means unbounded, and there are no overlaps per key. This is the convention already used by `CoursePointTimingAssociation` and `TransponderOwner`. Edits close, split or insert versions. They never mutate a version in place, except to correct a version's interval, which is itself a change event. Each owning service publishes one change event per atomic edit through its outbox. The event carries the **affected interval and scope**, not just before/after state.

### 4.2 Inventory

| Item | Owner | Today | Change |
|---|---|---|---|
| Timing point ↔ course point | Facilities | time-bound ✓ | Statistics consumes `CoursePointTimingAssociationsChangedV1` (fixes C4). |
| Transponder ownership | Timing | time-bound ✓ | Keep the existing reconcile, and run it inside the recompute worker so there is one writer. |
| **Facility layout in use** (which layout(s) a facility runs at time t) | Facilities | implicit (all layouts, and exactly one must match) | New `FacilityLayoutAssignment(FacilityId, CourseLayoutId, ValidFrom, ValidTo)`. Distance resolution uses only the layouts active at t. |
| **Layout geometry** (segments, order, lengths, closed loop) | Facilities | edited in place | `CourseLayoutRevision(LayoutId, ValidFrom, ValidTo, Segments[])`. Segments belong to a revision. A geometry edit creates a new revision from an effective date, and backdating is allowed. |
| **Installation ↔ facility** | Timing | mutable `Installation.Facility` | `InstallationFacilityAssignment(InstallationId, FacilityId, ValidFrom, ValidTo)` plus `InstallationFacilityChangedV1`. |
| Timing point ↔ installation | Timing | created from agent layout | Treat as immutable. A loop moved to another installation gets a new timing point. |
| **Required course points / required layout** | Statistics | mutable; edit-blocked once results exist | Move to `SimpleStatisticsConfigVersion`. Remove the edit block, because a new version from date X triggers a recompute from X. |
| **Item distance, distance mode, start/end course points, Multi repetitions** | Statistics | mutable | The same versioned config (`StatisticsConfigVersion` for Simple and Multi). An entry references the version it was computed with. |
| **Duration/speed thresholds** (global and per-config) | Statistics | mutable; history revalidated using current values | **Decision** (§6): version them like the rest, which is the requirement read literally, or keep "current thresholds apply to all history" as today. Recommended: version them. Then `StatisticsRevalidationService` and the `StatisticsValidityThresholdsChangedV1` flow fold into the recompute worker. |
| Statistics item/config existence (when a definition starts counting) | Statistics | none | A version's `ValidFrom` gives this for free. |
| Manual invalidations | Statistics | per entry | Not config. They stay attached to the entry by deterministic ID (§5.4). |

### 4.3 Statistics keeps a local, time-bound config replica

Recompute must not call Facilities over HTTP per sample (S3). Doing so is slow for bulk replays, and it is non-deterministic if Facilities changes mid-recompute. Instead:

- Statistics stores a read-only replica of the time-bound Facilities data (associations, layout assignments, layout revisions with segments) and Timing data (installation ↔ facility, ownership). It is updated by those services' change events. Each event carries the full new versions for the affected key, so applying it is an idempotent upsert keyed by version ID, and order does not matter.
- On a fresh deployment, or when the replica is suspected stale, a bootstrap snapshot endpoint per owner lets Statistics rebuild the replica, after which it recomputes everything.
- Distance and closed-loop logic moves from `FacilitiesService.CalculateCoursePointDistance` into a shared pure library (e.g. `Module.Facilities.Interface` or a new `Module.Course` package) that both Facilities and Statistics use. The rules then live in one place.

## 5. Behaviour details

1. **Required-point attempts** stop being frozen. Recompute re-evaluates them with the passings and config active at the time. This reverses an earlier "no late reassessment" decision, as both the existing plan and the new requirement call for.
2. **Equal timestamps**: order by `(Time, PassingId)`. Two passings at the same instant at the same loop for the same transponder count as one passing. Equal times at different loops keep both, and the zero-duration sample is dropped as invalid.
3. **Missing config at an event's time** (for example, no association yet): the passing stays a fact but produces no sample. It is counted in a per-session "unresolved" metric. When the config is later added or backdated, the change event makes the recompute pick it up.
4. **Manual invalidations** survive a recompute when the entry's deterministic ID still exists. If a manually marked entry disappears or its values change, the worker keeps the decision in a `ManualDecisionReview` row and shows it on the existing admin entries page. It never silently drops or transfers a decision.
5. **Ownership** is applied in the recompute step from the replica, so `TransponderOwnershipChanged` simply becomes another dirty-range trigger.
6. **Caching**: Statistics read caches are invalidated per affected facility/item after each recompute commit (existing `StatisticsCache` tags).

## 6. Open decisions (recommendation in bold)

1. **Version duration thresholds by time** (requirement read literally), or keep current-thresholds-for-all-history.
2. `SessionGap` default: **30 min**, configurable.
3. **Confine Multi windows to one session**, or allow cross-session chaining.
4. **Close #132** and reuse its parts, or merge it first as an interim duplicate fix and accept a second derived-data reset.
5. Whether anything outside Statistics needs result-change events now. **Nothing does today**, so publish none until a consumer exists.

## 7. Phases

Each phase deletes derived Statistics data and lets the recompute worker rebuild it from facts. Each phase is a branch from `v2` with a PR to `v2`.

**Phase 0: Tests that pin the requirements.** Using PostgreSQL fixtures, cover: P1/P3/P2 and fully shuffled arrival yield identical results to in-order arrival (property test over permutations); duplicate delivery of every message; agent replay of a whole day; backdated association, layout, installation and config edits; manual invalidation preservation and review. These tests fail on `v2` today.

**Phase 1: Facts and identity.** Add `PassingObservedV1` with `SourcePassingId`, plus Timing dedupe and conflict flagging. Add the Timing outbox and `PassingRecordedV1`. Delete `TimingSampleComplete`, Timing `Sample`, and the last-passing cache. Make `SampleEndpoint` window-based. Update the dummy agent and topology. Reset the Timing derived tables.

**Phase 2: Statistics recompute core.** Add `PassingFact`, `RecomputeRequest`, the session builder, the worker, deterministic IDs and diff-apply. Port the Simple, required-point, course-layout and Multi derivation into pure functions over an ordered session, so the existing reader rules are kept and their tests reused. Remove the `SampleComplete`/`EntryCreated` chain and handlers. Use the current (non-versioned) config at first, with a local replica of associations only. Reset `statistics.sample`/`statistics_entry`.

**Phase 3: Time-bound Facilities config.** Add `FacilityLayoutAssignment`, `CourseLayoutRevision`, the shared distance library, `CourseConfigChangedV1` (covering association, assignment and revision changes), the snapshot endpoint, and admin UI effective-date editing that reuses the association editor's interval and conflict-confirmation pattern. Add the Statistics replica and dirty-range triggers. Delete existing layouts and seed them again as unbounded revisions; Bootstrap reseeds.

**Phase 4: Time-bound Timing and Statistics config.** Add `InstallationFacilityAssignment` with its event. Add `StatisticsConfigVersion` (required points, layout, distance, repetitions, and thresholds if decision 1 says so). Drop the structural edit block. Fold threshold revalidation and ownership reconcile into the worker.

**Phase 5: Operations.** Add an admin "recompute range" action and a progress view. Add metrics (dirty backlog, oldest dirty age, unresolved passings, review backlog, outbox age) and DLQ alerts. Document the runbook.

Phases 1 and 2 must land together, or 2 directly after 1, since Statistics consumes the new fact event. Phases 3 and 4 are independent of each other once Phase 2 is in.

## 8. Acceptance criteria

- For any permutation and any duplication of the same passing set, stored samples and entries are identical, row for row and ID for ID.
- Replaying an agent's full history creates zero new rows.
- Backdating any versioned config item to time X changes only results with events at or after X within the affected scope, and those results match a fresh in-order run with the new config.
- A result never uses a config version whose interval excludes the time of the event it was derived from. For a sample, the start and end are each resolved at their own time.
- Manual invalidations are preserved or surfaced for review, never lost.
- No synchronous cross-service HTTP calls on the recompute path.
