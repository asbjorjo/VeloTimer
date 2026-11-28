# Messaging reliability and replay: multi-stage plan

## Problem and approach

Improve the Agent → Timing → Statistics pipeline so duplicate delivery is safe, late data produces correct results, and operators can replay historical data without stopping live ingestion or reads. Keep Azure Service Bus and, for now, SlimMessageBus. Split agent software control/lifecycle traffic from hardware-originated traffic. Establish and validate every shared contract, routing rule, and failure semantic **before** parallel service implementation.

Starting evidence: [agent-to-statistics research](./agent-timing-statistics-messaging.md), which is a historical snapshot of commit `8068a51650ad85734781c1413391f5e1cee379a5`. Revalidate against the actual implementation base before acting; do not assume every finding still applies.

## Confirmed decisions

- The application is alpha and not in production. Breaking changes and, if necessary, deletion of existing data are acceptable. Do not delete data as a routine step or without verifying the exact target and obtaining approval for the destructive action.
- Retain Azure Service Bus. Do not introduce a different broker as part of this plan. Keep SlimMessageBus unless feasibility testing exposes a concrete blocker requiring a separate decision.
- Keep hardware-originated passings, hardware layout/configuration, and loop/transponder status on `velotime-agent`. Use `velotime-agent-control` for commands and events about agent software, including pause/resume and agent lifecycle. Do not invent new lifecycle behavior solely to populate the topic.
- Correct results affected by late events. Recalculate affected required-point classifications, even though they are currently frozen. Events outside the configurable automatic-correction horizon are durably retained and flagged for manual review rather than silently discarded.
- Make source-event replay retention configurable, defaulting to 30 days from durable ingestion. Make automatic late-event correction configurable, defaulting to 24 hours. Define the precise correction-clock boundary in the contract gate.
- Replay uses configuration effective at event time. Retain the historical evidence needed for that result; current configuration alone is not sufficient.
- Conflicting payloads with the same agent/source passing ID must be durably flagged for manual review, not silently overwritten.
- Affected manual invalidation decisions must be flagged for review during correction/replay. Do not silently drop, transfer, or reinterpret those decisions.
- Replays must run online, preserving live ingestion and reads. Provide an authenticated operator UI and service APIs for replay and review.
- Equal-timestamp ordering, replay-storage architecture, and control-subscription provisioning are explicitly deferred to the contract/design gate; do not select them by assumption.

## Stage 0 — Baseline and contract discovery

1. Create a dedicated branch and worktree from the up-to-date appropriate base; preserve existing user changes and reuse this worktree for sequential handoffs. Independently validate the research findings against that base, including any intervening replay changes.
2. Inventory every producer, consumer, event, command, existing event identity, topic, subscription/filter, agent outbox, broker setting, and relevant API. Include external agent integration points and the dummy agent. Identify which lifecycle contracts are declared versus actually produced.
3. Inventory the current retention, historical Facilities association/layout data, Statistics definitions and threshold changes, ownership, required-point evidence, and manual decisions. Identify what is missing for event-time historical replay and how each service can expose its own data without cross-service database access.
4. Record current behavior and focused reproducible tests for P1/P3/P2, duplicate processing, failures at commit/send boundaries, and manual corrections. Inspect available deployment and test infrastructure; do not claim unverified broker guarantees.

**Exit:** current-state inventory and concrete gaps, with source references and any changed findings called out. This is investigation only, not service implementation.

## Stage 1 — Resolve and approve shared contracts

One contract owner coordinates Agent, Timing, Statistics, Facilities, platform, UI, and testing stakeholders. The owner produces one consistent contract package and obtains user approval for unresolved behavioral or architectural decisions. No dependent service implementation may begin until the package is validated and approved.

### Decisions to resolve with the user

- Equal event-time passings: order using a domain-valid tie-breaker, or flag ambiguity for review; specify effects on zero-duration samples.
- Compare service-owned replay sources and online rebuild mechanisms, including a Timing-owned journal/shadow projection and viable alternatives. Choose an authoritative source, ownership, write boundary, historical reference-data strategy, generation/cutover mechanism, availability, recovery and rollback guarantees.
- Compare agent-owned versus platform-owned control-topic subscription lifecycle, authorization, provisioning, removal, stale subscriptions, and per-agent isolation; decide before implementation.
- Define the 24-hour lateness clock and handling of future/invalid timestamps and clock skew. Specify compatible retention/correction settings and the behavior when historical evidence is unavailable or has expired.
- Define the operator decision workflow for ID conflicts, over-horizon events, and manual invalidations: available actions, authorization, audit, result visibility, and how an operator-triggered replay interacts with the horizon.
- Define how event-time configuration changes and retroactive configuration corrections affect already calculated results; distinguish missing history from an explicit configuration absence. Decide how user-visible reads behave during build, review, and cutover.
- Confirm stable source-ID uniqueness and reuse semantics with external agent implementers; define collision and retry behavior without assuming hardware provides a globally unique ID.

### Required contract package

- Canonical versioned event/command schemas for the existing flow and any required lifecycle, correction, and review/replay interactions: field types, UTC and SI conventions, source and event identity, causation/correlation, logical revision or equivalent, and exact JSON examples.
- Per-message routing matrix: producer, consumers, topic, subscription, filters, `MessageName`, agent addressing, session/partition key, transport message ID, and permitted ordering assumptions. Separate control-plane commands/lifecycle from hardware events; preserve the intended ownership of layout/status.
- Delivery and processing semantics: transaction boundaries, durable deduplication/inbox, outbox, retries, conflict handling, broker settlement, expiry/dead-letter and re-drive policy, retention, poison-message handling and observability. Specify what is verifiable rather than claiming exactly-once transport.
- Late-insertion and correction protocol, including predecessor and successor changes, invalidation/supersession, idempotent revisions, stale and out-of-order corrections, affected Statistics and Multi windows, required-point recalculation, and manual-decision review.
- Replay and historical-configuration contracts across service boundaries: retention, event-time lookup/versioning or immutable evidence, source cutoff and catch-up, replay generation, idempotency namespace, consistency/cutover/rollback, concurrent live updates, progress and failure reporting.
- Operator-facing API/client DTOs and authorization policy for preview, start, progress, failure, review, resolution and audit; UI states and error semantics. No destructive maintenance or replay action is implicitly authorized by approving the plan.
- Explicit rollout/reset strategy for alpha, including broker entities and pending/DLQ messages, existing databases, external agent packages, configuration names, and deployment sequencing. Breaking changes are allowed; a reset is conditional and separately authorized.

**Gate:** contract owner validates routing, serialization, identity, test vectors, compatible retention/horizon configurations, correction examples and API shape with the relevant owners. Resolve material changes through the contract owner, revalidate, and get user approval before releasing affected implementations.

## Stage 2 — Shared contract artifacts

After Stage 1 approval, implement and validate shared Interface/DTO artifacts, versioned schemas/examples, route constants/configuration contract, typed client/API contract surfaces, and contract tests. Agree precise non-overlapping file ownership; merge this stage into the integration branch before parallel consumers/producers begin. Do not add business behavior ahead of the contract gate.

**Exit:** buildable contracts and representative serialization, routing and semantics tests; published artifacts and compatibility/reset decisions available to every owner.

## Stage 3 — Parallel implementation workstreams

Start these only after Stage 2 passes. Each editing specialist works in a separate worktree/branch, tests its own changes, and submits focused commits for explicit integration. Shared files and schema ownership must be assigned in advance; nobody independently revises a shared contract.

1. **Agent and hardware ingestion:** separate hardware and control configuration/routing; implement agreed identity and conflict semantics, persistent enqueue/outbox and replay-source capture where assigned; update external agent integration and dummy agent. Preserve atomic persistence and expose failure/blocked-outbox signals.
2. **Timing processing and storage:** implement transactional deduplication, passing persistence, correction/reconciliation of predecessor/successor samples, revision protection, and outgoing outbox in the Timing-owned transaction. Implement the assigned replay-source or replay orchestration responsibilities and defined manual-review state.
3. **Statistics processing and storage:** implement transactional inbox/domain/outbox boundaries; idempotent sample and entry processing; consume corrections and rebuild all affected Simple, required/layout and Multi results. Flag affected manual invalidations and preserve review/audit information according to the agreed contract.
4. **Historical reference data:** Facilities and Statistics owners implement their agreed event-time version/evidence ownership and service APIs; include association/layout, definitions, thresholds and other evidence identified at the gate. Specify ownership-history handling via Timing rather than direct database access.
5. **Platform and deployment:** provision and configure `velotime-agent-control`, hardware and downstream topics, rules and subscriptions under the selected lifecycle model. Update AppHost, Helm, secrets/permissions, retries, DLQ/TTL/retention, telemetry and operator documentation. Verify routing isolation and topology reconciliation without removing undeclared live entities.
6. **Operator experience:** implement authenticated replay/review endpoints, typed clients and Blazor UI using approved contracts and service boundaries. Show selection, historical replay scope, status, conflicts, progress, failures, manual decision review, and access-denied states; protect action endpoints server-side.

Use contract fixtures or test doubles to permit parallel development. End-to-end functionality and cross-service behavior are not considered complete until the relevant service implementations are integrated.

## Stage 4 — Integration and operational verification

1. Integrate the reviewed workstreams in dependency order, preserving isolated ownership and resolving shared-contract changes through Stage 1's owner.
2. Test against PostgreSQL and a real isolated Azure Service Bus namespace using explicitly configured test credentials; verify topic/subscription routing, agent addressing, sessions, lock/retry/settlement behavior, DLQ and re-drive, and restart/crash boundaries. Never rely on inferred broker defaults.
3. Verify online replay with a fixed input boundary and catch-up under concurrent live writes; confirm result consistency, bounded checkpoints, progress, recoverability, operator visibility and rollback/cutover behavior.
4. Test historical event-time config changes and missing evidence; the configured 30-day ingestion-retention and 24-hour correction defaults, nondefault settings and boundary conditions; manual review for over-horizon events and ID conflicts; affected manual invalidations; and correct P1/P3/P2 and Multi reprocessing.
5. Test duplicate deliveries, repeated corrections, concurrent writers, out-of-order and stale revisions, equal-time behavior once decided, replay idempotency, control/hardware routing isolation, permissions, and retention expiry.
6. Run targeted existing test projects first, followed by applicable CI baseline and release build. Document exact results and environment-dependent gaps. Update runbooks, contract documentation, dashboards and alerts for lag, blocked outbox, expired source evidence, failures and review backlog.
7. Conduct a read-only review of the integrated diff and resolve substantive findings. If resetting data or replacing existing broker entities is necessary, present the exact impact and obtain approval before execution.

**Exit:** all contract and integration acceptance tests pass, operator workflows work end to end, observability and recovery are documented, and no known correctness gaps remain.

## Todos and dependencies

1. Baseline discovery → contract/design gate → shared artifacts.
2. After shared artifacts: Agent, Timing, Statistics, historical-reference, platform, and operator workstreams can proceed in parallel, subject to declared file ownership and approved interfaces.
3. Integration and real-broker validation depend on all applicable workstreams; final review follows verified integration.

## Notes

- Plan decisions not explicitly confirmed remain gate questions, not implementation assumptions.
- The research includes a shadow-rebuild proposal, but the user requested comparing alternatives before choosing it.
- Service-owned storage, UTC/backend SI units, authorization, user-facing locale conversion, and separate worktrees/atomic commits follow repository conventions.
