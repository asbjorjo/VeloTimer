# Time-bound timing-point/coursepoint associations

Status: Requirements confirmed; ready for implementation approval. Planning only; no repository changes authorized or made.

## Problem and approach

CoursePoint currently stores one mutable nullable TimingPoint GUID. Introduce Facilities-owned effective-time association records so reassignment preserves historical lookup. Include Facilities API/client, both admin UI entry points, and Statistics event-time resolution. Preserve current read DTO shapes; default reads resolve at one captured UTC now.

Backend/persistence times remain UTC; API timestamps use DateTimeOffset. Admin input/display follows browser locale or explicit preferences. Effective-time history is distinct from audit history.

## Confirmed requirements

- Include all three areas: Facilities API, admin UI and Statistics processing.
- Allow past, present and future effective dates.
- Preserve one-to-one mapping at every instant, with existing same-facility validation.
- Require explicit confirmation before replacing an occupied destination; do not silently displace another timing point.
- Support intervals unbounded in either direction. Migrate each existing mapping with neither a lower nor an upper bound, so it applies at every instant until explicitly changed.
- An omitted lookup timestamp means the current mapping, not the latest scheduled record.

## Confirmed scope boundaries

- Backdated edits must eventually trigger recalculation of existing Statistics. This plan adds the integration-event contract and reliable event publication, but does not implement the recalculation consumer, replay, sample repair or aggregate rebuilding. Until that follow-up is delivered, previously stored Statistics can remain stale after a correction; document that limitation in API/admin guidance.
- Edits affect only the specified interval, preserving prior mappings outside it. Omitted effective time on the legacy immediate-write path means now, not unbounded history. Explicitly unbounded interval input must be distinguishable from an omitted timestamp.

## Repository findings

- Model: src/Module.Facilities/Model/CourseLayout.cs. Actual EF migrations are under src/Module.Facilities/Migrations, not the migration worker project.
- Read surfaces: CoursePointEndpoints.cs, CourseLayoutEndpoints.cs, FacilityEndpoints.cs and FacilitiesService.cs.
- Writes/deletion guards: FacilityAdministrationEndpoints.cs. Reuse its timing-link authorization policy, same-facility validation and PostgreSQL advisory transaction lock.
- Shared write contract: SetCoursePointTimingLinkRequest in FacilityAdministrationDTO.cs.
- Facilities client is NSwag-generated from the API project. Preserve operation names and assess generated method compatibility.
- UI entry points: CourseLayoutEditor.razor and InstallationEditor.razor, shared services, BFF and cache snapshots. InstallationEditor currently has an unlink-then-link flow; replace it with an atomic Facilities operation.
- Cache layers: Facilities HybridCache, API output cache and WebUI. TTL-only caching may give stale current mappings when a scheduled boundary passes without another write.
- Statistics TimingSampleHandler currently resolves both endpoints using current mapping; incoming messages already provide TimeStart and TimeEnd.

## Design and proposed operation semantics

### Temporal model and reads

- Half-open intervals [validFrom, validTo), supporting null lower and upper bounds as negative/positive infinity. Require validFrom < validTo when both are finite; compare normalized UTC instants at database-supported precision.
- Migrate existing mappings as fully unbounded intervals. Do not invent historical change dates; this is the explicitly requested baseline.
- Capture UTC now once per read operation. Explicit past/future lookup is supported.
- Add optional at queries to timing-point lookup and mapping-bearing coursepoint/layout/facility reads. Preserve JSON shapes: unmapped timing-point lookup returns 404; an existing unlinked coursepoint returns 200 with null TimingPointId.
- Distance lookup supports independent start/end instants for samples spanning a reassignment, with coherent now defaults. Reject incompatible facility/path combinations explicitly.
- Derive all mappings from association records, rather than maintaining a second mutable current field.

### Finalized admin schedule/history read contract

- [OpenAPI contract](../api/facilities-temporal-associations.openapi.yaml): `GET /api/facilities/coursepoint/{id}/timing-associations`, operation ID `GetCoursePointTimingAssociations`.
- Reuse `AdministrationAuthorization.TimingLinkPolicy` (`CoursePointTimingLink.Write`): authenticate and require `velotime.admin` or `coursepoint-timing-link.write` before checking whether the coursepoint exists. Return bodyless 401/403 for authentication/permission failures and bodyless 404 for a missing coursepoint.
- Return 200 with a JSON array of the existing `CoursePointTimingAssociationSnapshot` DTO. Include all complete past, current and future association intervals from one consistent read, without an `at` filter or pagination; return `[]` for an existing coursepoint with no intervals.
- Bounds are half-open `[validFromUtc, validToUtc)`. Finite bounds are UTC `DateTimeOffset` instants with zero offsets; required nullable bounds mean negative/positive infinity, not omitted/default-now. Sort by lower bound ascending (null first), then upper bound ascending (null last). Gaps mean unlinked; do not synthesize snapshots for gaps.
- This is effective association history/schedule, not audit history: it does not expose superseded revisions, edit timestamps or actors. Both admin entry points use this list to show historical/scheduled intervals separately from the current mapping returned by the existing as-of read. Existing DTOs, reads and immediate writes are unchanged.
- NSwag generates `GetCoursePointTimingAssociationsAsync(Guid id, CancellationToken cancellationToken = default)` returning `Task<ICollection<CoursePointTimingAssociationSnapshot>>`. The API owner must implement and expose this operation before regenerating the runtime Facilities client; this revision adds no runtime handler.

### Interval changes and confirmation

- Provide an explicit interval-write contract supporting unbounded start/end, distinct from legacy writes whose omitted effective time means now. Keep existing clients' immediate link/unlink behavior and authorization. Finalize concrete routes and DTOs in the contract-first phase.
- Confirmed edit behavior: assign/unlink over the requested interval only. Close, truncate or split prior intervals as necessary, preserving mapping outside the edited interval. A finite end can therefore restore an earlier mapping; an unbounded end does not.
- Moving a timing point closes its previous association over the affected interval atomically. Preserve temporal uniqueness for both timing-point and coursepoint IDs.
- Detect all conflicting destination associations throughout the requested interval, including future schedules, rather than checking only the start instant. Return a structured 409 conflict/preview identifying affected mappings and intervals when replacement is unconfirmed.
- Both admin entry points display the affected assignments and interval before asking for confirmation. A confirmed retry must identify the exact conflict state the admin approved using a server-validated revision/token or equivalent precondition. Do not use a blind force flag that could replace a newly changed occupant.
- Revalidate conflicts and confirmation atomically under the existing transaction/locking pattern; stale confirmation returns a fresh conflict requiring reconfirmation. Confirmed replacement ends only overlapping portions of displaced mappings and preserves their remaining history.
- Unlinking preserves history. Define deterministic no-op/retry and same-timestamp behavior; never create empty or overlapping intervals.
- Present effective dates, optional ends and unbounded choices clearly in browser-local time, exchanging UTC with the API. Show scheduled/backdated changes separately from the current mapping so saving a future association does not misrepresent it as current.

### Persistence, dependencies and caching

- Enforce interval integrity and temporal one-to-one uniqueness in PostgreSQL as well as transactional application checks. Confirm range/exclusion-constraint deployment prerequisites before choosing the concrete mechanism.
- Preserve history through deletion: update point/layout/facility/timing-point dependencies to prevent history loss; block destructive operations as needed.
- Backfill must fail explicitly on conflicting legacy one-to-one mappings rather than guessing.
- Prefer bypassing time-sensitive mapping caches initially unless expiry reliably honors the next effective boundary at every layer. Retain safe geometry caching. Explicit-time results must not share a current-mapping key. Backdated writes must invalidate any affected historical lookup entries too.

### Statistics

- Resolve the start timing point at TimeStart and the end timing point at TimeEnd, independent of delivery/processing time. Reuse the validated Facilities distance contract without unnecessary timing-event changes.
- Test delayed and newly processed historical messages after backdated edits, and samples spanning a reassignment.
- Automatic repair of persisted samples/aggregates is required as a follow-up, not implemented here. Emit the integration event defined below so that follow-up has a stable, durable input.

## Implementation todos

1. Use the confirmed requirements and acceptance criteria. At implementation start, select an appropriate up-to-date base and dedicated branch, preserving existing user work.
2. Contract-first phase (.NET API Developer): finalize DTOs, unbounded-versus-omitted semantics, lookup parameters, independent distance times, structured conflicts and stale-safe replacement confirmation, errors, mapping-change integration-event contract and generated-client compatibility. Create and validate actual shared/OpenAPI artifacts before dependent implementation.
3. Persistence (Data & Migrations Developer): association entity/configuration, EF migration/snapshot, fully unbounded legacy backfill, indexes, database interval constraints, transactional event outbox storage and history-preserving deletion behavior. Fail explicitly on conflicting legacy data.
4. Facilities (.NET API Developer): shared as-of resolver, transactional interval edits and confirmed atomic replacement, consistent projections, dependency checks, cache correctness, cancellation and authorization. Regenerate and compile client consumers.
5. Consumers and messaging after validated contracts: Messaging & Workers Developer owns reliable outbox dispatch and bus registration; coordinate any Facilities API/processor/deployment wiring with the relevant owner.  Blazor UI Developer owns both admin entry points plus shared services/BFF, localized date input and conflict confirmation UX; Messaging & Workers Developer also owns Statistics lookup at TimeStart/TimeEnd. Do not add a recalculation consumer. Give shared files one owner; independent implementations may run in parallel after the contract gate.
6. Verification (Tester): add regressions and execute smallest relevant builds/suites, including real PostgreSQL migration/constraint and outbox durability tests. Set OIDC_AUDIENCE=https://build.invalid where needed. Return production defects to original owners.
7. Documentation and review: update directly related API/admin documentation, especially interval boundaries, backdating, unbounded baseline, replacement confirmation and Statistics consequences. Code Reviewer performs read-only substantial-change review against tests and acceptance criteria. Resolve findings through owners, rerun affected checks and inspect final diff.

## Acceptance and verification

- At T-epsilon return A; at T return B. Default lookup crosses the boundary without another write/cache eviction; scheduled mappings never appear prematurely.
- Existing mappings resolve at past/current/future instants after migration, with neither bound. Support bounded, lower-unbounded, upper-unbounded and fully unbounded intervals.
- Historical A remains queryable outside explicitly edited intervals after reassignment/unlink. Both directions and nested projections use consistent as-of resolution.
- Test backdated/future edits, finite-end restoration, interval splitting, gaps, exact boundaries, normalized offset-equivalent instants, precision, same-timestamp writes and retries.
- Occupied destination yields explicit conflict with no write before confirmation; confirmed replacement is atomic and history-preserving. Test future overlaps, stale confirmation and concurrent confirmed replacements.
- Verify permissions, invalid timestamps/IDs, same-facility validation, concurrent conflicts/rollback, legacy backfill/conflicts and deletion protection using PostgreSQL, not an in-memory substitute.
- Test warmed Facilities/API/WebUI caches across a future boundary using a controlled clock, and invalidation after backdated corrections.
- Verify delayed Statistics messages and samples spanning a move with distinct start/end resolution. Verify event publication supports future recalculation without implementing that consumer.
- Compile generated-client consumers. Run targeted Facilities domain/API tests, affected worker/platform/UI checks and Release builds; expand only as warranted. Report unavailable infrastructure separately from verified passes.

## Out of scope unless separately agreed

Course geometry/name versioning, broad audit-history reconstruction and unrelated infrastructure/API refactoring. Stored Statistics recalculation implementation (consumer, replay, sample/aggregate repair) is explicitly deferred; its integration-event interface and durable producer are included.

## Integration-event interface for deferred recalculation

### Ownership and proposed contract

- Facilities owns a versioned public event in Module.Facilities.Interface/Messages, provisionally CoursePointTimingAssociationsChanged. Validate the actual artifact with both Facilities and messaging owners before implementation depends on it. Reuse the repository's EventId plus before/after change-list convention where appropriate; do not assume Timing's publishing path already provides a reusable Facilities outbox.
- Proposed fields: EventId (Guid), OccurredAtUtc (DateTimeOffset), FacilityId (Guid), a stable change/revision identifier for reconciliation, and Changes containing nullable Before/After association snapshots. Each snapshot contains TimingPointId, CoursePointId, nullable ValidFromUtc and nullable ValidToUtc. Bounds are half-open; null bounds mean unbounded, never unspecified/default-now.
- The change list describes every effective mapping delta from the atomic edit, including source removal, destination displacement, unlinking and interval splitting/restoration. Group all deltas of one atomic edit under the same event/change identity. Include enough interval information to identify the exact affected windows, without forcing a future consumer to infer them from processing time or current mapping.
- Publish changes for backdated, immediate and future edits, not just backdates; retries that make no effective change do not create another logical event. Migration baseline backfill is not an admin correction and does not enqueue bulk recalculation events.
- Document future consumer semantics: deduplicate by EventId; tolerate reordered delivery; use affected timing-point IDs and event-time windows to find impacted samples, matching either endpoint independently. Start and end may use different associations. Include displaced timing points, not only the requested one. Reconcile against authoritative Facilities mappings rather than blindly applying stale before/after state. No consumer or repair job is implemented in this plan.

### Reliable publication boundary

- Persist the event/outbox entry in the same PostgreSQL transaction as the mapping edit. A direct publish after commit alone can lose the recalculation signal; a publish before commit can signal rolled-back changes.
- Add a Facilities-owned dispatcher using existing SlimMessageBus conventions and available repository infrastructure where suitable. Delivery is at least once with a stable EventId across retries, explicit failures/logging and acknowledgement after successful publication. Account for crash/restart and multiple dispatcher instances; do not claim exactly-once delivery.
- Verify where durable bus retention is configured. Absence of a future consumer must not be presented as guaranteed replayability: if the deployed bus requires a pre-created durable subscription, provision the inactive durable destination or explicitly arrange a replay source/retention policy before relying on notifications for later repair. Do not implement the recalculation consumer to satisfy this concern.
- Assign storage/migration changes to Data & Migrations Developer, write-transaction integration to .NET API Developer, dispatcher/bus wiring to Messaging & Workers Developer, and deployment-specific broker resources to Platform & Deployment Developer only where required. Complete the contract-first gate before dependent tasks.

### Additional acceptance tests

- A committed effective mapping edit persists one logical event containing complete before/after affected intervals, including displaced assignments. Test bounded/unbounded edits and backdates.
- A rejected edit, unconfirmed/stale conflict, rolled-back transaction or effective no-op emits no change event.
- Broker unavailability does not lose a committed edit or its pending event. Restart/retry eventually publishes with the same EventId; duplicate delivery is permitted and documented.
- Test dispatcher concurrency/claim behavior and acknowledgement failure boundaries. Verify contract serialization preserves nullable bounds and UTC timestamps.

## Association concurrency response recovery (PR #117)

The explicit `PUT /api/facilities/coursepoint/{id}/timing-association` keeps its
structured `TimingAssociationConflictPreview` 409 contract on PostgreSQL exclusion,
check, foreign-key, deadlock, and serialization failures. After the failed edit's
transaction is disposed/rolled back, recovery clears failed tracked state and reads
source and destination in a new repeatable snapshot. It returns `confirmation-stale`
with the normalized requested interval, current conflicts (possibly empty), and a
token bound to the complete current overlapping source state as well as the request
and destination conflicts. Recovery never saves, enqueues, or automatically retries
the edit. Explicit confirmation revalidates under the normal advisory write lock.

If the coursepoint has disappeared, recovery returns the existing 404; invalid
references return the existing 400. Cancellation and unexpected recovery failures
propagate rather than fabricating a preview. The legacy `timing-link` endpoint keeps
its documented ProblemDetails 409.

Regression: `TimingAssociationConcurrencyResponseTests` uses the fixture's disposable
PostgreSQL database and an EF interceptor to commit independent source/destination
changes, then raise each real PostgreSQL failure inside the edit transaction after
EF has saved/accepted its changes but before event enqueue. It verifies rollback,
no event/cache invalidation, current conflicts, generated-client 409 deserialization,
request/source-bound stale rejection, and successful explicit confirmation, including
an unconfirmed unlink recovery with empty conflicts.

Run with a **test-only** PostgreSQL admin connection whose database is `postgres`:

```sh
FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION='Host=localhost;Port=5432;Database=postgres;Username=postgres' \
OIDC_AUDIENCE=https://build.invalid \
dotnet test src/Module.Facilities.Api.Test/VeloTime.Module.Facilities.Api.Test.csproj \
  --configuration Release --filter 'FullyQualifiedName~TimingAssociation'
```

The fixtures create and drop their own uniquely named databases; never point them
at a production server.
- Build/validate bus registration and any required durable destination configuration without adding Statistics recalculation processing. Report that stored Statistics repair remains intentionally deferred.
