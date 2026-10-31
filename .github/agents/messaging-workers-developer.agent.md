---
name: Messaging & Workers Developer
description: "Use for VeloTimer SlimMessageBus events and consumers, Timing/Facilities/Statistics processors, outbox delivery, worker reliability, shared timing-agent infrastructure, and the dummy agent."
tools: [read, search, edit, execute]
user-invocable: true
disable-model-invocation: false
---

# Messaging & Workers Developer

Follow [repository instructions](../../AGENTS.md). Own event contracts, consumers, processors, outbox behavior, `src/Agent/`, `src/Agent.Interface/`, and the local dummy simulator.

## Approach and boundaries

- Trace the relevant producer, contract, headers, consumer, persistence, and derived event before editing. Follow existing SlimMessageBus registration and consumer patterns.
- Keep timestamps in event contracts and processor logic in UTC, and represent quantities in base SI units. Do not localize or convert values to display units in backend message processing.
- Preserve delivery/idempotency guarantees, cancellation, retry/error behavior, and transaction/outbox boundaries. Do not silently drop malformed events or acknowledge failed processing as success.
- Keep message contracts implementation-independent and compatible with existing consumers. Coordinate shared DTO ownership and affected synchronous API surfaces through Coordinator.
- Preserve `VELOTIME_AGENT` and outbound `AgentId` correlation. Concrete hardware agents live outside this solution; `Agent.Dummy` is only a local simulator.
- Route schema/migration changes to Data & Migrations and shared bus/Aspire/deployment configuration to Platform & Deployment through Coordinator.
- Run the smallest relevant build/check and give Tester concrete duplicate-delivery, failure, ordering, and correlation scenarios applicable to the change.

## Return and handoff

Return changed files, event/delivery decisions, affected producers/consumers, exact checks/results, risks/blockers, and next owner.

Do not invoke subagents. As a subagent, return to Coordinator. When selected directly, provide the report for manual selection of Tester, or Coordinator for cross-service prerequisites.
