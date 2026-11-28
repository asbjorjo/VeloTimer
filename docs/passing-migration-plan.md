# Legacy passing migration and derived-data regeneration

## Problem and agreed approach
Create a separate .NET 10 one-shot `Passings.Migration` worker, following Users.Migration hosting/container conventions. Read raw passings from legacy PostgreSQL velotimerdb and replay them as PassingEvent messages through the existing Timing and Statistics processors. Do not copy legacy samples, statistics, or aggregates: regenerate derived data in the destination.

Use message-bus replay rather than an API that duplicates processor behavior. This preserves the existing event-driven path and requires no new HTTP ingestion surface. Implementation remains subject to the reliability gate below; unmodified processor replay is not yet proven safe.

Plan finalized for the maintenance-window approach. Planning only: no repository changes or execution approval.

## Confirmed requirements
- Assume a maintenance window for affected transponders: pause live ingestion, drain pre-existing affected messages, freeze relevant destination configuration, and keep the window open until replay and downstream processing have drained and been verified.
- Migrate raw legacy passings; generate destination derived data through normal processing. Exclude legacy derived rows from migration input.
- Legacy TimingLoop.LoopId == PassingEvent.LoopId == destination TimingPoint.SystemId.
- Legacy Transponder.SystemId == PassingEvent.TransponderId == destination Transponder.SystemId.
- Require configuration mapping destination agent IDs to legacy track slugs.
- Migrate configured tracks only; explicitly report unmapped tracks and excluded passing counts in the selected period. Unmapped tracks are intentional exclusions, not migration failures.
- Migrate all history for configured tracks by default.
- Optional FromDate and ToDate are inclusive UTC calendar dates, with no sub-day configuration required.
- Require the destination to exist and be configured; no silent auto-provisioning for migration.
- Include Aspire and Helm/container delivery.
- Legacy velotimerdb is the only legacy database dependency. No identity database, SQL Server/ASP.NET Identity dependency, or Keycloak administration. Destination authenticated read-only preflight may use existing service clients without using legacy identity.

## Proposed configuration
Dedicated PassingMigration section:

    PassingMigration:
      AgentTrackMappings:
        actual-agent-id: legacy-track-slug
      FromDate: null
      ToDate: null

- Reject empty mappings, blank identities, unknown configured track slugs, and multiple agents mapped to one track slug, since source rows cannot identify the correct agent in that case.
- Resolve source track through Passing.Loop.TrackId and Track.Slug; map to AgentId. Do not infer AgentId from Passing.SourceId.
- Serialize numeric LoopId as invariant string; preserve transponder SystemId exactly, including leading zeroes. Validate supported transponder timing systems; use destination installation/timing-system plus loop identity, not global loop ID.
- Optional ISO dates YYYY-MM-DD become Time >= FromDate UTC midnight and Time < midnight after ToDate. Either bound may be omitted. Reject invalid/reversed dates and an unrepresentable upper boundary. Preserve complete source timestamp precision.
- Warning flags absent from legacy data are false; do not overwrite existing destination flags on retries.
- Use the existing message-bus connection and topic conventions rather than introducing arbitrary transport configuration. The selected runtime topic must match the Timing consumer.

## Evidence and reliability gate
- PassingObservedHandler receives AgentId from message headers, registers passing and sample, publishes PassingSaved/TimingSampleComplete, and updates installation last-seen. This matches the clarified regeneration aim.
- Agent publishing sets AgentId and MessageName headers; PassingEvent SessionId is TransponderType_TransponderId. Preserve this existing session convention and validate transponder-type serialization.
- Timing consumer uses session-enabled Azure Service Bus and permits multiple concurrent sessions. Publish in ascending (Time, legacy Id) order across all selected tracks so each transponder session receives chronological messages; do not independently replay tracks in parallel when transponders overlap.
- AgentService deduplicates raw passings but the handler can republish derived events on retries. TimingSampleHandler currently inserts a statistics sample on every message; end-to-end replay idempotency is not established.
- AgentService automatically creates missing installations, timing points, and transponders; migration requires stricter validation before any source messages are published and enforcement when migration messages are consumed. Keep normal live-agent auto-provisioning intact.
- Inserting historical rows amid existing samples or live traffic can invalidate sample relationships. Require a maintenance/exclusive replay window for affected transponders and validate pre-existing history before publishing. Validate existing history against the intended chronological replay and fail explicitly on incompatible sample boundaries or insertions requiring historical sample rebuilding. Compatible duplicates from the same replay must remain retryable; do not reject all existing rows indiscriminately. Do not assume an API solves this problem.
- Installation last-seen is updated with historical times by the normal handler. Validate that replay cannot regress operational last-seen; make only tightly coupled migration-safe changes if required.
- Broker acceptance is not processor completion. The worker must report publication completion accurately; end-to-end success requires separate processor/DLQ checks and reconciliation, or a minimal completion protocol if required. Do not claim derived data is ready when publishing finishes.

## Destination prerequisites
Validate configured agents/installations, compatible timing systems, loop/timing points, and required transponders before publication. Validate Facilities course-point/distance mappings and applicable Statistics configuration because downstream regeneration depends on them. Ownership-dependent derived results require existing migrated ownership/profile data when relevant; no direct legacy identity access is introduced and the replay worker does not run users migration itself.

Missing/ambiguous destination configuration must fail preflight, not auto-create resources. Processor-side migration enforcement must prevent a configuration deletion/race from reverting to live auto-provisioning. A migration header or contract addition, if necessary, belongs to the contract owner and must preserve normal event compatibility.

## Acceptance criteria
- One-shot worker reads legacy database without modifying its rows/schema; publishes only raw PassingEvent messages for configured tracks and selected UTC days.
- Every message has correct AgentId, MessageName, transponder type, transponder ID, loop ID, timestamp, and session metadata.
- Legacy derived tables are never imported; Timing/Statistics regenerate samples and derived results using existing logic.
- Bounded no-tracking source projections and deterministic keyset paging by (Time, Id), preserving order across configured tracks. No whole-history materialization or growing offset scans.
- Required destination configuration is preflighted; migration cannot provision missing destination entities at consume time.
- Report selected source counts, unmapped track/count exclusions, broker-accepted messages, failed publication, and replay status without conflating publication with processing.
- Cancellation and failures are explicit/logged; worker stops on publication completion and exits nonzero on failures.
- Safe retry semantics are validated through the entire processing chain, not merely raw-row deduplication or broker duplicate detection. Recovery after publication/process crash must not duplicate derived data or lose required downstream events.
- Do not introduce a new database solely for checkpointing without a demonstrated requirement. Evaluate deterministic message IDs and existing broker duplicate detection as supporting mechanisms, not substitutes for consumer idempotency.
- Validate overlap/live-traffic prerequisites and preserve timestamp precision, destination flags, and operational last-seen semantics.
- Aspire resource is explicitly started after genuine prerequisites. Helm delivery is a dedicated opt-in migration job, not an automatic replay on every unrelated upgrade. No legacy identity configuration.

## Todos and ownership
1. Planning complete: use processor replay during the confirmed maintenance window, with existing-history compatibility and retry safety validated before implementation.
2. Prepare a dedicated branch from the agreed up-to-date base, preserving user work. Earlier clean worktree inspection showed v2 behind its remote; recheck at implementation.
3. Contract-first/reliability gate — Messaging & Workers Developer owns transport artifacts and existing consumer behavior review. Confirm PassingEvent serialization, headers/session order, migration identification/enforcement, destination preflight strategy, natural-key retries, downstream deduplication/recovery, and completion evidence. Define actual compatible artifacts and focused checks before dependent implementation. If API/client read surfaces are missing, route those artifacts to .NET API Developer before releasing dependent work. Any required unique indexes or persistence changes belong to Data & Migrations Developer. Do not promise safe replay without passing this gate.
4. Source phase — Data & Migrations Developer owns minimal legacy EF context/model/query within the new worker. Verify actual table/column mappings, UTC precision, configured-track filtering, excluded-track counts, date bounds, and chronological keyset paging. Avoid referencing the identity-heavy Users.Migration executable or copying its entire graph. This phase can proceed independently of transport design after branch preparation.
5. Replay worker — Messaging & Workers Developer owns project/startup/options/mapping/preflight orchestration/publishing/reporting/shutdown. Consume validated contract and source artifacts; use existing SlimMessageBus provider/serialization patterns with multi-agent per-message headers and existing sessions, not ConfigureAgent's single-agent identity/outbox database registration wholesale.
6. Platform delivery — Platform & Deployment Developer owns solution/AppHost/publishing/Helm and related README. Supply velotimerdb, MessageBus and actual preflight client credentials/references; no legacy identity. Define opt-in invocation and genuine processor/Facilities/Statistics prerequisites, monitoring, DLQ checks, configured tracks/date examples, maintenance requirements, and publication versus regeneration completion.
7. Tester — Add regression and integration tests for exact UTC inclusive boundaries, all-history defaults, configured/unmapped tracks, mapping ambiguity, cross-track loop IDs, transponder identity/type, equal timestamp ordering, paging, headers/sessions, empty input, missing destinations with no provisioning, retries/crashes/concurrent duplicates and derived event recovery, cancellation/failure exits, no legacy derived-row imports, and actual downstream regeneration. Run targeted worker/domain/processor/platform checks with OIDC_AUDIENCE=https://build.invalid and affected Release builds. Use PostgreSQL/broker integration where necessary; report unavailable external-service validation as a blocker, not a pass.
8. Coordinator and Code Reviewer — Reconcile specialist evidence and selected-source versus destination counts on controlled fixtures. Read-only review substantial changes after validation, route defects to owners, rerun affected checks, and verify end-to-end derived results before completion.

## Dependencies
- Plan approval precedes branch preparation.
- Contract/reliability and source phases follow branch preparation, independently with non-overlapping ownership.
- Replay implementation requires validated transport/reliability and source artifacts; any added API/data contracts are prerequisites for affected implementations.
- Platform delivery requires agreed dependencies and worker project metadata.
- Validation requires implementation/delivery. Review and acceptance require test and reconciliation evidence.

## Scope boundaries and implementation gate
Concurrent live ingestion and arbitrary historical sample rebuilding are out of scope for this maintenance-window migration. Compatible reruns are in scope. Validate the selected history against existing destination samples before publishing; reject incompatible history explicitly. The contract owner must demonstrate safe replay and downstream recovery before releasing the worker implementation. No further feature-scope clarification is required; wait for an explicit request to implement.

## Notes
No repository edits, builds, migration runs, or production data access during planning. No time estimates. Keep reliability changes tightly scoped to replay and preserve live-agent behavior. Destination ownership/history prerequisites must be documented without adding an identity-database dependency to this worker.
