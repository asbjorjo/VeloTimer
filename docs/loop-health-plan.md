# Plan: loop status history and loop health graphs for admins

Base: `v2` at `15f82de5` (2.1.0-alpha.56, after replay-safe Phase 5 #152). Branch from and target `v2`.
Status: approved plan (owner, 2026-11-28). Nothing here is implemented yet.

## Goal

Admins can see how each timing loop has behaved over time: noise, signal quality of the transponders it
reads, and how many transponders were read with low signal strength or low hits. They can tell a loop
problem (many transponders weak on one loop) from a transponder problem (one transponder weak on every loop).

## What happens today

| Layer | Loop status | Passing signal quality |
|---|---|---|
| Contract (`Agent.Interface/Messages/Events.cs`) | `LoopStatusEvent(Time, LoopId, Noise)`. No other fields. | `PassingObservedV1` carries only the booleans `LowBattery`, `LowStrength`, `LowHits`. No numeric strength or hits. |
| Agent library (`src/Agent`) | `Model/LoopStatus` + `MessagingService.SendEventAsync(LoopStatus)` enqueue to the agent outbox. Not stored locally. | Flags stored in `agent.passing` and sent. |
| Dummy agent | Sends no loop status. | Sends no flags. |
| Timing.Processor | `LoopStatusEventHandler` only calls `UpdateLastSeenAsync`. **Noise is discarded.** | Flags stored on `timing.passing` (and on rejected passings and conflicts). |
| Timing.Api / UI | Installation list shows `LastSeen` only. | `/transponder-status/{id}` shows a transponder's warning passings (owner/admin). No per-loop view. |

Other findings:

- `TransponderStatusEvent` is defined in the contract but nobody produces or consumes it. Dead code.
- Every loop status event calls `UpdateLastSeenAsync`, which writes the installation row and invalidates its
  cache entry. Passings do the same. At a status cadence of a few seconds per loop this is a lot of writes
  and cache churn for one timestamp.
- The `infrastructure` subscription is not session-enabled, so loop status can arrive out of order. That is fine
  once storage is idempotent and order-independent (below).
- No charts exist in the UI yet. MudBlazor 9.5 (already referenced) has `MudTimeSeriesChart` and `MudChart`,
  so no new package is needed.
- The concrete X2 and P3 agents live outside this repo, so which decoder fields they can send must be
  settled there (see Confirmed decisions).

## Decoder data worth capturing

What MYLAPS decoders typically expose (to confirm per agent; field names from the P3 protocol, X2 SDK differs):

| Source | Field | Use |
|---|---|---|
| Status record (per decoder/loop) | Noise | Core loop health graph. Already in the contract. |
| | Loop triggers | Activity / interference indicator. |
| | Decoder temperature | Overheating in enclosures; correlates with noise. |
| | Input voltage | Power supply problems. |
| | GPS / time sync state, satellites in use | Clock accuracy. A decoder that loses sync drifts, which silently corrupts lap times. High value. |
| Passing record | Strength (numeric) | Signal graphs, SNR estimate, weak-tail tracking. Today only the `LowStrength` boolean arrives. |
| | Hits (numeric) | Read margin. Today only `LowHits`. |
| | Low battery flag | Already sent; include in counts. |

**Recommendation:** the single most useful addition is numeric `Strength` and `Hits` on passings. Noise alone
says little; strength relative to noise is what tells you a loop is degrading, and it works for every passing.

## Design

### 1. Contract (non-breaking JSON additions)

Keep the message names so the external agents keep working, and add optional init-only properties:

```csharp
public record LoopStatusEvent(DateTime Time, string LoopId, double Noise)
{
    public int? LoopTriggers { get; init; }
    public double? DecoderTemperature { get; init; }   // kelvin (SI); UI shows °C
    public double? InputVoltage { get; init; }         // volt
    public bool? TimeSynchronised { get; init; }       // GPS/time sync locked
    public int? SatellitesInUse { get; init; }
    public string? DecoderId { get; init; }            // for tracing only
}

public record PassingObservedV1(...)
{
    // existing flags...
    public double? Strength { get; init; }             // decoder units, as reported
    public int? Hits { get; init; }
}
```

- Noise and strength have no SI unit. They are stored as the decoder reports them, with the scale documented
  in the XML comment. SNR is only computed when strength and noise share a scale (to confirm per decoder).
- Delete `TransponderStatusEvent`.
- Agent library: add the fields to `Model/LoopStatus`, `Model/Passing` (+ migration on `agent.passing`), and
  `EventMappers`. Dummy agent: send status every 5 s per loop with random-walk noise, and passings with
  strength/hits, occasionally low, so dev has data to graph.
- Renaming to `LoopStatusObservedV1` would match `PassingObservedV1` but needs a topology change and a lockstep
  agent release. Not worth it now.

### 2. Timing storage (replay-safe without a source ID)

New table `timing.loop_status` (raw samples):

| Column | Notes |
|---|---|
| `id` | uuid PK |
| `installation_id`, `timing_point_id` | resolved like passings (create timing point if unknown) |
| `time` | decoder time, UTC, normalised like `PassingTime.Normalize` |
| `received_at` | Timing clock |
| `noise`, `loop_triggers`, `decoder_temperature`, `input_voltage`, `time_synchronised`, `satellites_in_use` | nullable except noise |

Unique index `(timing_point_id, time)`. A status sample has no decoder sequence number, and the
`(loop, decoder time)` pair is already a natural key, so no UUIDv5 source ID is needed. The handler does
`INSERT ... ON CONFLICT DO NOTHING`; a redelivery or full replay inserts nothing.

New table `timing.loop_status_rollup`, one row per `(timing_point_id, bucket_start)` with a 5-minute bucket:
`samples`, `noise_sum`, `noise_min`, `noise_max`, the same for temperature and voltage, `triggers_sum`,
`unsynchronised_samples`. These are all mergeable aggregates. In the **same transaction** as the raw insert,
and only when the insert actually inserted a row, the handler upserts the bucket (`samples + 1`,
`least/greatest`, sums). Because the raw insert deduplicates first, replay and out-of-order delivery give the
same rollup. Coarser buckets (1 h, 1 day) are aggregated from the 5-minute rows at query time.

Retention: raw `loop_status` is kept indefinitely (owner, 2026-11-28). At an assumed 5 s cadence that is
about 6.3 million rows and roughly 1 GB (with indexes) per loop per year if the decoder runs around the
clock, and a third of that at 8 h a day. Charts read the rollup, so query cost does not grow with the raw
history. Keeping raw rows means rollups can always be rebuilt from them, and no "too old" drop rule is
needed. If storage ever matters, add partitioning by month or a retention setting then.

Passings: add nullable `strength` and `hits` to `timing.passing`, `rejected_passing` and `passing_conflict`, and
include them in the conflict comparison (they are part of the immutable fact). Existing rows stay null; no
backfill. Add a partial index `(timing_point_id, time) WHERE low_strength OR low_hits OR low_battery` so low
signal counts stay cheap.

Passing-derived health (counts, distinct transponders, mean and 10th percentile strength) is computed at query
time with `date_bin` over `timing.passing`, using the existing `(time)` index plus a new
`(timing_point_id, time)` index. Velodrome volumes (tens of thousands of passings per busy hour) make this fine
for ranges up to months, and it avoids a second rollup that would need dirty-bucket tracking for late
passings. If it gets slow, add a passing rollup later using the Statistics recompute-request pattern.

`LastSeen`: write it only when it advances by at least 30 s (`Timing:LastSeenResolution`). The UI still shows
"seen in the last minute" correctly, and status no longer churns the installation cache.

### 3. Metrics and runbook (Phase 5 style)

A DB-sampled monitor in Timing.Processor (same pattern as the Statistics backlog gauges, aggregate with `max`):

| Instrument | Meaning | Suggested alert |
|---|---|---|
| `velotime.module.timing.loop.status_age` `{installation, loop}` (s) | Time since the last status sample | Warn above 2 min while the installation is active |
| `velotime.module.timing.loop.noise` `{installation, loop}` | Mean noise over the last 5 min | Above a configured threshold for 10 min |
| `velotime.module.timing.loop.low_signal_ratio` `{installation, loop, kind=strength\|hits}` | Share of passings in the last 15 min with the flag | Above 20% with at least 20 passings |
| `velotime.module.timing.loop.unsynchronised` `{installation, loop}` | 1 when the latest sample reports no time sync | Any value 1 for 5 min |

Add `docs/timing-loop-health-runbook.md` with the signals, what each usually means (noise: interference,
cabling, nearby equipment; low ratio on one loop: loop or decoder; one transponder low everywhere: chip
mounting or battery), and where to look in the UI.

### 4. Timing API (admin, `TimingPolicy`)

- `GET /admin/loop-health` — current state per installation and loop: last status time, latest noise, time
  sync, passings and low-signal ratios over the last hour. Feeds the overview page.
- `GET /admin/installations/{id}/loop-health?fromUtc&toUtc&bucket=auto|5m|1h|1d&timingPointId=` — series per
  timing point. Per bucket: noise min/avg/max, temperature, voltage, unsynchronised samples, passings,
  low-strength / low-hits / low-battery passings, distinct transponders, distinct low-strength and low-hits
  transponders, mean and p10 strength, mean hits, and SNR estimate where defined. `auto` picks 5 min up to
  2 days, 1 h up to 60 days, then 1 day. Cap the number of buckets returned.
- `GET /admin/installations/{id}/loop-health/transponders?fromUtc&toUtc` — transponders with low strength or
  low hits in the range: counts per kind, total passings, ratio, how many loops it was weak on. Sorted by ratio.
  Links to the existing transponder status page.

Add the typed client methods to `Module.Timing.Client` and the BFF service used by the admin pages.

### 5. Admin UI (MudBlazor)

- **Loop health overview** `/admin/timing/loop-health`, linked from the Timing admin nav and from each row in
  *Installations*. A table of all loops grouped by installation with status chips (OK / stale / noisy / weak
  reads / no time sync) and a small noise sparkline.
- **Installation loop health** `/admin/timing/installations/{id}/loop-health`:
  - Range presets (1 h, 6 h, 24 h, 7 d, 30 d) plus custom, timing point multi-select. Auto refresh every 30 s
    when the range ends now.
  - Charts (`MudTimeSeriesChart`, one series per loop): noise (avg with min/max), mean and p10 strength with
    SNR, passings split into normal / low strength / low hits, distinct transponders with low strength and
    with low hits. Decoder temperature, voltage and sync appear only if any sample has them.
  - Table of weak transponders for the range (from the transponders endpoint).
- Times in browser locale and temperature in °C, converted in the UI per `AGENTS.md`. Localise all strings in
  the existing resource files.

## Phases (one PR each)

1. **Contract and agent library**: optional fields, agent migration, mappers, dummy agent data, delete
   `TransponderStatusEvent`. Tests: mapper and outbox round trip.
2. **Timing storage**: `loop_status`, rollup upsert in the handler, passing strength/hits, conflict
   comparison, indexes, `LastSeen` throttling. Tests: replay twice gives one row and the
   same rollup; shuffled order gives the same rollup; conflict on differing strength.
3. **Metrics and runbook**.
4. **API and client**: endpoints, auth, bucket selection, query tests on PostgreSQL (integration job).
5. **Admin UI**: pages, charts, nav links, rendering tests in `VeloTime.Platform.Test`.

Data: nothing to migrate or delete. Loop status was never stored and existing passings simply have null
strength and hits.

External follow-up (agent repos): send numeric strength and hits on passings, and the extra status fields
each decoder offers.

## Other signals to consider later

1. **Missed reads per loop.** Using the course point order from Facilities, a transponder seen at the loops
   before and after a loop but not at it is a likely missed read. A rising miss rate is the most direct
   "this loop is failing" signal and needs no new decoder data.
2. **Delivery lag.** Add `received_at` to passings to graph decoder time to Timing arrival, which shows agent
   outbox backlogs and network trouble.
3. **Decoder clock offset.** If agents also send their own receive time with each status sample, Timing can
   track decoder clock drift against the agent host.
4. **Per-transponder trends.** With numeric strength, a rider's chip getting weaker across sessions points to
   a dying battery or a bad mount before it starts missing laps.
5. **Rejected passings and conflicts over time** per installation, already stored, as an extra chart.

## Confirmed decisions (owner, 2026-11-28)

- "Low" means the decoder's own `LowStrength` / `LowHits` / `LowBattery` flags. Timing applies no thresholds of
  its own to numeric strength or hits; those are only graphed.
- Loop health pages and endpoints are for the Timing admin role only (`AdministrationPolicies.Timing` /
  `TimingPolicy`).
- The contract offers all the fields above as optional values. Which ones each X2/P3 agent fills, its status
  cadence, and whether noise and strength share a scale (needed for SNR) are for the agent implementers to
  settle. Timing and the UI must handle any field being absent: hide a chart with no data, and show SNR only
  when the agent declares a shared scale.
- Raw loop status samples are kept indefinitely, alongside the 5-minute rollups.
