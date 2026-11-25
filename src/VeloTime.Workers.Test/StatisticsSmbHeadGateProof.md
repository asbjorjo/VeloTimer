# Stock SMB with domain head release: follow-up to 777f8561

Historical experiment report. Production conversion is now documented in
`StatisticsSmbProduction.md`; these retained proof algorithms remain test-only,
but their fixture now uses production registration and its isolated schema.

This is **test-only**, not a production conversion. See
`StatisticsSmbHeadGateProofTests.cs`, another partial of the existing PostgreSQL
fixture. It uses stock SMB **3.4.0**, real PostgreSQL, actual Statistics migrations,
real scoped EF/outbox transactions, production producers/serialization/header
modifiers, and the controlled Azure `ServiceBusSender`. No custom polling worker,
repository, SQL selection template, inbox, contract or production schema was added.
`proof_effect` is only a transactional domain-effect counter, not a work queue.

## Assessment before implementation

The previous experiment published **every** receipt to SMB and proved that stock
sequence alone cannot enforce domain processing order. That does **not** rule out
head release: eligibility can be decided by the domain transaction **before**
publishing, rather than by a custom SMB selector. A producer persists all receipts,
but calls `Publish` only for the oldest unprocessed receipt in that rider's stream.
The consumer atomically completes that head and publishes its immediate successor.
The same advisory lock must cover insertion, local order allocation, completion,
head selection and commit. There is then no notification gap requiring another
worker: either completion sees the insertion, or insertion sees the completed stream.

This was feasible with the already-demonstrated shared scoped transaction. The
remaining question was dispatch while processing outruns sender acknowledgement.
The experiment deliberately commits successor publication **inside** the first
sender's acceptance callback, in a different scoped transaction, then holds the
sender return. It does not defer consumer processing until `delivery_complete`.

## Executed evidence

There are **16 new cases**, all parameterized over SampleComplete and EntryCreated:

| Cases | What is asserted |
|---|---|
| 2 successful acknowledgements | Persist three receipts, enqueue only P. Accept P, commit its domain effect/completion and enqueue S while P sender is held; T remains unqueued. Across several poll intervals only P is accepted. After return, accepted sequence is P,S,T; S observes P's persisted delivery mark, T observes S's. |
| 2 lost acknowledgements | Same critical race; sender throws **after** transport acceptance, repeatedly. While P remains incomplete in SMB, all accepted messages are P duplicates, S stays queued, T unqueued. Fresh provider restart with a **different instance/lease ID**, repaired sender and retained database eventually accepts P duplicates,S,T. Effects and SMB mapping counts remain exactly three. |
| 2 delivery-mark failures | A real PostgreSQL trigger rejects `delivery_complete=true` updates after P was accepted and processed. S is committed but not accepted. P is retried, with no second effect. Remove trigger, dispose/rebuild provider, recover P duplicates,S,T. |
| 2 domain rollback / broker fail-stop simulations | Throw **after** saving completion/effect and enqueueing successor but before commit: all three roll back, and successor never reaches SMB. An acknowledged but unprocessed/dead-lettered head leaves the successor durably unreleased. Another rider can publish. Explicit head redrive releases the successor. |
| 4 insert/completion races | Hold the real advisory lock in either insert-first or completion-first transaction. Verify the contender backend is actually waiting on PostgreSQL's `advisory` wait event. Insert-first completion sees S; completion-first sees an empty stream and later insertion releases S. Exactly one S mapping, accepted P,S, no missed work. |
| 2 pre-accept transport aborts | Small test-only attempt limit makes SMB set `delivery_aborted`; receipt and pending SMB evidence remain, no false delivery/processing success, no S enqueue. Another rider publishes. Explicit test operator repair of the retained aborted row recovers P and then S. |
| 2 accepted/processed but ack-lost aborts | P processes and releases S, then SMB eventually aborts P after lost acknowledgements. S **does** publish/process, with P's delivery mark still false. Accepted order is P duplicates,S. Operator repairs P and an actual late P duplicate is accepted **after S** without another effect or enqueue. |

Both adapters use their real immutable receipt payloads and completion columns:
`SampleCompleteOutbox.EntriesCreatedAtUtc` and
`EntryCreatedOutbox.MultiDecisionsCreatedAtUtc`. The Entry stream also sends a
third event with absent `Single`, following two with `Single=true`. Stable receipt
IDs, message type, GUID SessionId, immutable payload and Single semantics are
validated before idempotent completion. A malformed body with otherwise matching
headers fails, including after the retained receipt is already completed.

The important normal-case transport evidence is **acceptance order**, not merely
order of `Publish` calls or reads of domain markers. The controlled sender records
acceptance before invoking concurrent processing and before returning/throwing.
Effects are counted in PostgreSQL, transactionally, with a unique receipt key.
Lost acknowledgement tests retain every duplicate acceptance; none is filtered
out to manufacture the ordering assertion.

## Why the configured poller behaves this way

Source reviewed at package repository commit
`c02e4a0b660b2d20be23fbbf08c5a47dacf2b457`:

* `OutboxSendingTask.SendMessages` awaits selection and `ProcessMessages`.
  `DispatchBatch` awaits transport completion, then awaits `UpdateToSent` (or
  records unsuccessful attempts), **before another selection**. With one poller,
  batch size one, no concurrent send of the newly committed successor occurs
  while the predecessor sender is held.
* PostgreSQL's `smb_outbox_lock_table_and_select` returns the oldest pending,
  non-aborted timestamp, limited to the configured batch size. Failed predecessor
  remains selectable; the tested later-timestamp successor cannot leapfrog it
  on transport error, update error or fresh-provider restart. New instance waits
  for old leases to expire. The fixture uses a five-second lease to bound this test.
* Aborted rows are excluded. Therefore **do not claim** an unconditional rule that
  every successor send requires its predecessor's `delivery_complete=true`.
  The accepted/processed/aborted case above deliberately disproves that stronger
  rule, while preserving first acceptance and domain processing order.

No downstream delivery-mark check is required by this algorithm. Successor release
itself proves predecessor processing committed; that processing followed an actual
accepted predecessor message. Retries may create delayed duplicates, which must
validate against retained immutable receipts and have no further effects.
`MaintainSequence` orders by SMB **timestamp**, not domain receipt ID, with no
explicit timestamp tie breaker. Clock regression/equal timestamps are not exercised:
the stronger assertion about **all duplicate sends preceding S / P delivery mark
preceding S** is not universal under those conditions. This does not invalidate
the causal first-acceptance barrier: S cannot be released until P has been accepted
and processed. No exactly-once transport or global cross-stream order is claimed.

## Runnable algorithm in the test

`InsertHeadReceipt`, `ProcessHeadReceipt`, `ReleaseHead`, and `HeadLock` are the
executable reference; they deliberately use separate adapters for the two streams.

```text
insert transaction:
  acquire statistics-transponder:{guid} advisory transaction lock
  insert immutable receipt(s), allocating local order while locked; save
  find oldest unprocessed receipt of this rider/stream
  if it has no retained SMB mapping: Publish that one concrete message
  commit all together

consumer transaction:
  acquire the SAME transponder lock
  resolve exactly one retained receipt; validate typed payload, ID, headers
  if completion already set: return idempotently (no second effects or release)
  require this receipt is the oldest unprocessed receipt in its stream
  perform domain work / all durable decisions, including zero-result completion
  save completion and effects
  find immediate next unprocessed receipt; Publish only if not already mapped
  commit domain effects, completion and successor SMB enqueue together
```

In the fixture, mapping means exactly one retained SMB row with typed
`MessageName` + `StatisticsReceiptId`. More than one fails explicitly. An existing
mapping, even aborted, is **not** replaced automatically. Retained mapping queries
participate in the same DbContext transaction. All participating receipt writers
must use the lock; no uncoordinated backfill, bulk insertion or manual release.
Production needs an approved durable unique mapping/association and retention
policy; test-only header lookup is not migration approval. Existing producer replay
must validate/reuse the winning receipt, not create another local order position.

Both streams share the rider lock but maintain their own head, as the existing
dispatchers do. A real SampleComplete transaction must also persist all derived
EntryCreated receipts and run the Entry head-release step before committing.
Entry processing may append derived receipts to its own stream under that same
lock; mark its current head and select the immediate successor only after saving
all work. Existing no-result/config-version/Single semantics must be preserved,
not inferred from an SMB delivery flag.

## Limits, recovery and next owner

* This is not real-handler/domain-geometry integration. The completion columns
  and immutable payloads are real, but `proof_effect` stands in for domain outputs.
  Full Multi decisions, empty config sets, derived outputs crossing both streams,
  producer replay conflicts and legacy reconciliation still need integration tests.
* No Azure server is involved: broker session acceptance ordering, settlement,
  lock renewal, delivery count and actual DLQ movement are **not** proven.
  The broker fail-stop case is an acknowledged head deliberately left unprocessed,
  followed by explicit local redrive, not an actual Azure dead-letter operation.
* Restart disposes the complete DI provider/poller and rebuilds it with a new lease
  identity against the retained DB. Abrupt OS kill, network partitions, cancellation
  at every await and PostgreSQL crash recovery are not exercised.
* Single instance/poller is the premise; no overlapping deployments or multiple
  processor pollers are certified. Isolate processor SMB schema from the separate
  API's `statistics.smb_outbox` (the existing isolation proof still runs).
* Poison unprocessed heads intentionally block their rider. Persisted receipts,
  mappings, aborted flags and completion markers expose the work; they do not
  schedule DLQ recovery. Monitor pending/aborted and delivered-but-unprocessed age,
  alert, fix and explicitly redrive the same stable identity. Do not mark complete,
  delete receipts, skip to S or synthesize successful effects to unblock a rider.
  Infinite transport retries can starve other riders with this one poller; global
  fairness is not promised. Finite aborts allow unrelated publication but need repair.

Next: manual **Tester** selection for independent rerun/review of this evidence;
**Coordinator** before any production conversion, with Data owning mapping/schema
and Platform owning isolated single-poller registration/rollout. This evidence
supports head-gated stock SMB as an alternative to the earlier inbox/custom-selector
proposal; it does not authorize removal of existing production dispatchers yet.

## Reproduce

Set `OIDC_AUDIENCE=https://build.invalid` and
`VELOTIME_TEST_STATISTICS_POSTGRES` to a PostgreSQL connection with create/drop
database rights. Each case creates/migrates/drops its own GUID database; the
supplied database is not migrated or reset. No credential is checked in.

```sh
dotnet test src/VeloTime.Workers.Test --no-restore --filter 'FullyQualifiedName~HeadGate'
dotnet test src/VeloTime.Workers.Test --no-build --no-restore --filter 'FullyQualifiedName~StatisticsSmbOutboxProofTests'
```

Final observed run on the supplied PostgreSQL endpoint: **22/22 proof cases**
(16 new + 6 original), **21/21 existing worker regressions**, and **15/15 API
threshold PostgreSQL/messaging tests**, all zero skipped/failed. Regression/API
filters are the exact commands in `StatisticsSmbOutboxProof.md`; the API fixture
uses `VELOTIMER_PUBLIC_STATISTICS_POSTGRES` instead. The targeted `dotnet test`
also built the changed worker test project successfully; no full-solution build
was run. An independent no-build repeat of all **16 new cases** also passed,
zero skipped/failed. Existing dependency advisory warnings remain.

During development, the first build exposed a missing namespace import, corrected
before execution. The first combined 22-case run exposed a test synchronization
error in the two new abort-after-processing cases: it queried the strict
single-row successor delivery lookup before processing had created that row.
The test now waits for the successor SMB insert before checking its delivery;
the strict missing/ambiguous-row failure behavior was not weakened.
