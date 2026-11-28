# Plan: passing replays from the source system, with per-agent capabilities

Base: `v2` at `02b9fe7` (after #153 and #154). Branch from and target `v2`.
Status: approved plan (owner, 2026-11-28). Phases 1 and 2 implemented; Phases 3 and 4 not started.

### Phase 1 as built

- Contracts and constants are in `Agent.Interface` (`AgentCapabilities`, `PassingReplayModes`, `PassingReplayStates`,
  `PassingReplayRejectionReasons`, `AgentControl`). `AgentStartedEvent` is deleted.
- `IPassingReplaySource` (with `PassingReplayRange`) is defined in `VeloTime.Agent.Replay` so X2 and P3 work can
  start. `Describe()` returns `null` while the source cannot replay. Nothing calls it until Phase 3.
- Capability entries come from registered `IAgentCapabilityProvider`s. Phase 1 ships only the pause/resume provider,
  so no agent advertises `passing-replay` yet; the `AgentStore` provider lands with its handler in Phase 2.
- Pause and resume consumers are always registered (`ConfigureAgent` runs before the agent registers its services),
  but the handlers resolve `IControlService` optionally and log and acknowledge when there is none. This removes the
  DI failure; the capability is advertised only when an `IControlService` exists.
- The `infrastructure` subscription gains the `AgentCapabilitiesV1` rule only. The `PassingReplayStatusV1` rule lands
  with its consumer in Phase 2.
- Timing also stores `installation.control_protocol_version`. The HTTP DTO gives `MaxRange` as `MaxRangeSeconds`
  (SI seconds); the message keeps `TimeSpan`.
- `POST /api/timing/admin/installations/{id}/capabilities/refresh` (`202`, `404`) sends `ReportCapabilitiesCommandV1`.

### Phase 2 as built

- The agent's `ReplayPassingsHandler` validates the command and stores it in `agent.replay_request`; a
  `PassingReplayWorker` runs it in the background and resumes interrupted replays on start. Chunks are read by
  (`time`, `id`) keyset; each waits until the outbox backlog is at most `Agent:Replay:MaxPendingOutbox` (default the
  chunk size). Every state change saves the row and enqueues `PassingReplayStatusV1` in one outbox transaction.
- `AgentStore` advertises `SupportsLoopFilter = true` (the store has the loop ID, so filtering is free) and
  `OldestAvailableUtc = min(agent.passing.time)`. It can be turned off with `Agent:Replay:AgentStoreEnabled`.
- New reason codes: `InvalidRange` and `LoopFilterUnsupported` (agent and platform), and the platform-only
  `NoCapabilityReport` and `NoteTooLong`. Timing also answers `Busy` itself when it has an active request for the
  installation; the agent still checks.
- Timing applies statuses in report order, never reopens a finished request, and lets a late agent status replace
  `Expired`, since the agent's answer is the truth. The expiry sweep runs every minute in Timing.Processor.
- The dummy agent now sends through `MessagingService`. Its old redelivery simulation is removed; replays cover that.
- The dummy agent's optional fake `Source` moves to Phase 3 with the `Source` mode.


An admin can ask an agent to send the passings for a time range again, either from the agent's own store or from
the source system behind it (decoder memory, timing server). Each agent says which of these it supports, and the
API and UI only offer what that agent has advertised. A replay is safe to run at any time: passings Timing already
has are counted as duplicates and create no rows; passings Timing never got are recorded and flow into Statistics
through the normal path.

Scope is **passing replays only**. Loop status, layout and transponder status replays are out of scope, though the
capability model leaves room for them.

## What happens today

| Area | Finding |
|---|---|
| Control contract (`Agent.Interface/Messages/Control.cs`) | `PauseAgentCommand(AgentId)` and `ResumeAgentCommand(AgentId)`. Nothing else. |
| Control publisher | **None.** No service in this repo publishes either command. |
| Agent consumer (`Agent/HostingExtensions.cs`) | Both commands are consumed from `velotime-agent` (the same topic the agent produces to), subscription named after the agent, SQL filter `agentId = '{agentId}'`. The header the bus actually sets is `AgentId`, so the filter casing must be checked. The agent provisions this subscription itself; `topology.json` does not declare it. |
| `IControlService` | No implementation in this repo outside tests. The dummy agent does not register one, so a pause command would fail in DI. |
| `AgentStartedEvent` | Declared, never produced or consumed. |
| Agent store (`agent.passing`) | Every passing sent through `MessagingService.SendEventsAsync` is inserted with `ON CONFLICT DO NOTHING`, and **only newly inserted passings are published**. No cleanup, so the store holds the agent's whole history. The dummy agent bypasses `MessagingService` and has no store. |
| Agent outbox | One outbox, `MaintainSequence`, `PollBatchSize = 1`. A large replay enqueued in one go would sit in front of live passings. |
| Passing identity | `PassingId.Create(agentType, identity)` (UUIDv5, #143). The Service Bus `MessageId` is `{agentId}:{SourcePassingId}`, so if broker duplicate detection were turned on, a resend inside its window would be silently dropped. The shipped topology does not enable it. |
| Timing dedupe (`AgentService.RegisterPassingAsync`) | Same installation + source ID + payload: `Duplicate`, nothing written or published. Same ID, different payload: `PassingConflict` (`SourcePassingIdPayloadMismatch`). Different ID, same transponder/loop/time: `PassingConflict` (`DuplicateObservation`). A resolved observation deletes an earlier `RejectedPassing` for that ID. Only `Recorded` publishes `PassingRecordedV1`. |
| Statistics | A new `PassingRecordedV1` marks a dirty range and the recompute worker handles it (replay-safe plan, Phases 1 to 5). No manual recompute is needed after a replay. |
| `TimingSystem` enum | `Unknown`, `MyLaps_X2`, `MyLaps_P3`. Descriptive, set by an admin, not reported by the agent. |

So the control plane is a stub: two commands with no sender, a subscription on the wrong topic, and no way for the
platform to know what an agent can do.

## Design

### 1. Two replay sources

| Mode | Where passings come from | Who implements it | What it fixes |
|---|---|---|---|
| `AgentStore` | `agent.passing` for the range | The shared agent library, once, for every agent that uses `MessagingService` | Passings the agent has but Timing lost or rejected (outbox purge, DLQ, a rejection that is now fixable, a Timing data reset). |
| `Source` | The decoder or timing server behind the agent | Each hardware agent, through a hook in the shared library | Passings the agent never got (agent offline, network drop, crash before insert). |

Both modes publish **every** passing in the range, not just new ones. Timing's dedupe makes that cheap and it is
the only way to recover passings Timing is missing but the agent store has. In `Source` mode the agent also inserts
what it reads into `agent.passing` (insert if absent), so a later `AgentStore` replay covers it too.

A replayed passing must get the same `SourcePassingId` it got live. For `Source` mode that is the hardware agent's
job: the identity it feeds `PassingId.Create` must come from fields the source returns identically on live and
replay reads. If it does not, every replayed passing becomes a `DuplicateObservation` conflict, which is safe but
useless. This is the main thing each hardware implementer must check.

### 2. Capability advertisement

Capabilities belong to the **agent**, not to `TimingSystem`. Two P3 agents can differ (agent version, whether the
decoder is reachable, how deep its memory is), and a future system type only needs to ship an agent that reports
honestly. The agent sends a report on startup, whenever a capability changes (for example a decoder connects or
disconnects), and when asked.

```csharp
namespace VeloTime.Agent.Interface.Messages.Events;

/// Sent on the agent topic; replaces AgentStartedEvent (deleted).
public record AgentCapabilitiesV1(
    DateTime ReportedAtUtc,
    string AgentType,           // the same constant passed to PassingId.Create, e.g. "P3", "X2", "Dummy"
    string AgentVersion,
    int ControlProtocolVersion, // 1 for this plan
    IReadOnlyList<AgentCapability> Capabilities);

public record AgentCapability(string Name, PassingReplayCapability? PassingReplay = null);

public record PassingReplayCapability(
    string Mode,                    // "AgentStore" | "Source"
    DateTime? OldestAvailableUtc,   // null when unknown
    TimeSpan? MaxRange,             // longest range one request may cover
    bool SupportsLoopFilter);
```

Capability names are string constants in `Agent.Interface/AgentCapabilities.cs`:

| Name | Meaning |
|---|---|
| `passing-replay` | One entry per supported mode, with its limits. |
| `control.pause-resume` | Present only when the agent registers an `IControlService`. |

Unknown names are kept and shown but not acted on, so new capabilities need no Timing release to be visible.

Timing stores the latest report on the installation: `installation.agent_capabilities` (jsonb),
`installation.capabilities_reported_at`, `installation.agent_type`, `installation.agent_version`. No history; the
latest report wins (compare `ReportedAtUtc`, ignore older). `InstallationDTO` gains a `Capabilities` block. An admin
can ask for a fresh report with `ReportCapabilitiesCommandV1`.

### 3. Control topic and routing

Move commands off the hardware topic, as `docs/research/agent-timing-statistics-messaging-plan.md` already
proposed.

| Message | Producer | Topic | Subscription | Filter |
|---|---|---|---|---|
| `ReplayPassingsCommandV1`, `ReportCapabilitiesCommandV1`, `PauseAgentCommand`, `ResumeAgentCommand` | Timing.Api (outbox) | `velotime-agent-control` (new) | one per agent, named after the agent | correlation filter on application property `AgentId` |
| `AgentCapabilitiesV1`, `PassingReplayStatusV1` | Agent (outbox) | `velotime-agent` | `infrastructure` (new rules) | `MessageName` |
| Replayed `PassingObservedV1` | Agent (outbox) | `velotime-agent` | `timing` (unchanged) | unchanged |

The topic goes into `topology.json`. The per-agent subscription stays agent-provisioned for now, as it is today, so
onboarding an agent needs no deploy (see decision 1). Commands set a Service Bus TTL equal to their expiry so an
offline agent does not run stale replays when it reconnects days later.

### 4. Commands and status

```csharp
namespace VeloTime.Agent.Interface.Messages.Control;

public record ReplayPassingsCommandV1(
    Guid RequestId,
    string AgentId,
    string Mode,                          // "AgentStore" | "Source"
    DateTime FromUtc, DateTime ToUtc,     // half-open [From, To), passing time
    IReadOnlyList<string>? LoopIds,       // only if SupportsLoopFilter
    DateTime ExpiresAtUtc);

public record ReportCapabilitiesCommandV1(Guid RequestId, string AgentId);
```

```csharp
namespace VeloTime.Agent.Interface.Messages.Events;

public record PassingReplayStatusV1(
    Guid RequestId,
    DateTime ReportedAtUtc,
    string State,          // Accepted | Rejected | Running | Completed | Failed
    int Read, int Published,
    int NewInAgentStore,   // Source mode only
    string? Reason);       // set for Rejected and Failed
```

The agent re-checks the request against its own current capabilities and rejects with a reason
(`UnsupportedMode`, `RangeTooLong`, `OutOfRetention`, `SourceUnavailable`, `Expired`, `Busy`). The platform's
check (below) is a convenience; the agent's check is authoritative because the platform's copy can be stale.

Replayed passings carry an application property `ReplayRequestId` (header, not payload, so payload equality and
dedupe are unaffected). Timing.Processor uses it to count outcomes per request (decision 2).

### 5. Agent library

```csharp
namespace VeloTime.Agent.Replay;

/// Implemented by hardware agents that can read history from their source. Optional.
public interface IPassingReplaySource
{
    PassingReplayCapability Describe();   // called for every capability report
    IAsyncEnumerable<Passing> ReadAsync(PassingReplayRange range, CancellationToken cancellationToken);
    event EventHandler? CapabilityChanged;
}
```

- `PassingReplayHandler` (shared) consumes `ReplayPassingsCommandV1`. `AgentStore` mode reads `agent.passing` by
  time (add an index on `time`). `Source` mode calls `IPassingReplaySource.ReadAsync`, inserts if absent, and
  publishes. Both publish through a new `MessagingService.RepublishAsync` that enqueues without the
  insert-if-absent filter.
- **Live first.** Replays enqueue in chunks (default 200 passings per transaction) with a short pause between
  chunks, so the single sequenced outbox never holds more than one chunk ahead of live traffic. One replay runs at
  a time per agent; a second request is rejected as `Busy`.
- Handled `RequestId`s are kept in `agent.replay_request` so a redelivered command reports its earlier result
  instead of running again. Running twice would still be harmless, only wasteful.
- `CapabilityReporter` (hosted service) sends `AgentCapabilitiesV1` on start, on `CapabilityChanged`, and on
  `ReportCapabilitiesCommandV1`. `AgentStore` is advertised with `OldestAvailableUtc = min(agent.passing.time)`.
- Pause and resume handlers register only when an `IControlService` is registered, which also fixes today's DI
  failure.
- Agent consumer subscription moves to `velotime-agent-control` with a correlation filter on `AgentId`.

The dummy agent switches to `MessagingService` (so it has a store and advertises `AgentStore`) and gets an optional
`Source` implementation behind a config flag that regenerates deterministic passings for a range, so tests and
local runs exercise both modes and the "not supported" path.

### 6. Timing

- New table `timing.passing_replay_request`: id, installation_id, agent_id, mode, from_utc, to_utc, loop_ids,
  requested_by_user_id, note, created_at, expires_at, state, agent counts (read, published, new_in_agent_store),
  Timing counts (recorded, duplicate, conflict, rejected), reason, updated_at.
- States: `Pending` (row written, command in outbox) → `Accepted` → `Running` → `Completed` | `Failed`, or
  `Rejected`, or `Expired` (no `Accepted` by `ExpiresAtUtc`; a sweep marks it).
- `POST /api/timing/admin/installations/{id}/passing-replays` (Timing admin policy) validates against the stored
  capabilities: the mode is advertised, the range is within `MaxRange` and after `OldestAvailableUtc`, the loop
  filter is supported, and a capability report exists. Failures return `422` with a reason the UI shows. Row and
  command go in one outbox transaction.
- `GET .../passing-replays` and `GET .../passing-replays/{requestId}` for status.
- `POST .../capabilities/refresh` sends `ReportCapabilitiesCommandV1`.
- Timing.Processor handles `AgentCapabilitiesV1` and `PassingReplayStatusV1` on `infrastructure`. Status updates
  ignore anything older than the stored `updated_at` (the subscription is not session-enabled, so order is not
  guaranteed).
- `PassingObservedHandler` reads `ReplayRequestId` and, in the same transaction as the registration, increments the
  matching outcome counter. Unknown request IDs are ignored.

Nothing changes in dedupe, conflict or rejection handling, and Statistics needs no change at all.

### 7. UI

On `TimingInstallations` / `InstallationEditor`:

- A capabilities panel: agent type and version, report age, each capability with its limits, and a refresh button.
  "No report yet" when the agent has never sent one.
- A **Replay passings** action, shown only when at least one replay mode is advertised. The dialog offers only the
  advertised modes, limits the date pickers to `OldestAvailableUtc` and `MaxRange`, shows the loop picker only when
  supported, and warns when the report is older than a day or the agent has not been seen recently.
- A replay history list with state and counts ("412 read, 3 recorded, 409 duplicates, 0 conflicts"). Conflicts
  link to the conflict review once that exists.
- Times shown in the browser's zone, sent in UTC.

### 8. Runbook

Add a "Replay passings" section to `docs/statistics-operations-runbook.md` (or a new Timing runbook): when to pick
`AgentStore` vs `Source`, what the counts mean, that a replay needs no follow-up recompute, and that many
`DuplicateObservation` conflicts from a `Source` replay mean the agent's identity derivation differs between live
and replay reads.

## Phases

| Phase | Content | Repo |
|---|---|---|
| 1 | Contracts (`AgentCapabilitiesV1`, commands, status, capability constants), delete `AgentStartedEvent`, `velotime-agent-control` topic and `infrastructure` rules, agent consumer move and filter fix, `CapabilityReporter`, Timing stores capabilities, DTO and UI panel. | here |
| 2 | `AgentStore` replay end to end: agent handler, `RepublishAsync`, chunking, `agent.replay_request`, Timing request table, endpoints, status handling, outcome counters, dummy agent on `MessagingService`, UI dialog and history, runbook. | here |
| 3 | `IPassingReplaySource` hook, `Source` mode in the handler, dummy fake source. | here |
| 4 | X2 and P3 agents implement `IPassingReplaySource` and report capabilities. | external agents |

Migrations are additive (new table, new installation columns), so no data needs deleting. Commits follow the
existing rule of no breaking-change markers on v2.

### Tests

- Capability report stored, older report ignored, DTO and UI reflect it.
- API rejects unadvertised mode, too long or too old range, unsupported loop filter, missing report.
- Agent rejects a request its current capabilities do not allow, and an expired command.
- `AgentStore` replay of a range Timing already has: all `Duplicate`, no new `timing.passing` rows, no
  `PassingRecordedV1`.
- Replay after deleting some Timing passings: exactly those recorded, Statistics recompute picks them up.
- Replay of a previously rejected passing that now resolves: recorded, `RejectedPassing` removed.
- Redelivered command does not run twice; live passings are not held behind a full replay (chunking).
- Routing: a command for agent A is not delivered to agent B.

## Confirmed decisions (owner, 2026-11-28)

1. **Per-agent control subscription.** The agent keeps provisioning its own subscription (as today), so new agents
   need no deploy.
2. **Timing-side outcome counts.** Replayed passings carry the `ReplayRequestId` header and Timing keeps
   per-request outcome counters, so the UI can say how many passings a replay actually recovered.
3. **Default limits.** `MaxRange` is 30 days for `AgentStore` and whatever the source reports for `Source`; one
   replay per agent at a time; command expiry 15 minutes.

## Questions for the X2 and P3 implementers

- Can the decoder or timing server return passings for a time range, or only by passing number range? If by number,
  the agent maps the time range to numbers using `HardwarePassingId` in `agent.passing`, and the mapping must cover
  gaps where the agent has no rows.
- How far back does the source keep passings, and can the agent read that depth to fill `OldestAvailableUtc`?
- Does a replayed record carry exactly the same fields the agent uses for `PassingId.Create` on live reads
  (decoder ID, passing number, time at the same resolution)? If not, which fields are stable on both?
- Does replaying disturb live reads on the same connection, or does it need a second connection or a pause?
- With several decoders behind one agent, is replay per decoder, and should `SupportsLoopFilter` map to that?
- Are low battery / strength / hits flags returned on replay the same as live? A difference becomes a
  `SourcePassingIdPayloadMismatch` conflict.
