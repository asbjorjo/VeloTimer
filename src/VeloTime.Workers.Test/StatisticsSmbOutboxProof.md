# Statistics SMB outbox prerequisite experiment

**Follow-up:** [Stock SMB with domain head release](StatisticsSmbHeadGateProof.md)
tests an alternative not exercised below: persist all domain receipts but publish
only the head, then atomically release the successor during processing. The
all-rows-published counterexample below does not rule out that design, nor does
it establish that an inbox or custom selector is required.

Scope: SMB **3.4.0**, branch `fix/statistics-sample-replay`, starting at
`51ec21c3`. This is test-only evidence, **not approval to remove either production
dispatcher**. No production registration, message contract, API, migration or
deployment configuration changes.

## Exercised boundary

`StatisticsSmbOutboxProofTests.cs` calls the actual processor `StartupExtensions.AddServices`
and keeps its serializer, header modifier and concrete `SampleComplete` /
`EntryCreated` producers. Only inbound consumer settings are removed, before
explicit bus start, so no Azure receivers are created. `AutoStartConsumersEnabled(false)`
alone does **not** prevent an explicit `IMasterMessageBus.Start()` starting consumers.
A controlled `ServiceBusSender` replaces Azure sending; PostgreSQL, EF transactions,
SMB enqueue, polling, transport construction and delivery updates are real.

The fixture creates a GUID-named **database**, applies Statistics migrations to
that empty database and creates `statistics_processor_proof`. It drops only its
own database, including on setup failure. The supplied administrative database
is not migrated/reset. SMB creates its tables but **requires the schema to exist**:
without fixture schema creation `Start()` failed with `MessageBusException:
Outbox schema migration failed`, inner PostgreSQL `3F000`.

The experiment re-enters `AddSlimMessageBus(...)` to register the new plugin's DI
services. Merely mutating the already-registered builder afterward did not register
`IOutboxMessageFactory` and allowed publication to bypass the outbox. Production
integration must put the outbox registration inside the original registration callback.

## API names and demonstrated results

* `UseOutbox(bool, Func<Type, bool>)` filters **message types**, not individual rows.
  The proof enables exactly `SampleComplete` and `EntryCreated`; every call publishes
  one concrete event, never a collection envelope.
* `AddOutboxUsingDbContext<StatisticsDbContext>(...)` is configured with
  `PostgreSqlSettings.DatabaseSchemaName = "statistics_processor_proof"`,
  `MaintainSequence = true`, `PollBatchSize = 1`, `MaxDeliveryAttempts = int.MaxValue`,
  and `MessageCleanup.Enabled = false`. The short poll interval is test-only.
* The scoped `IOutboxMessageFactory` resolves to
  `DbContextOutboxRepository<StatisticsDbContext>` and
  `IPostgreSqlTransactionService` to `DbContextTransactionService<StatisticsDbContext>`.
  Their `DbContext` properties are the **same scoped instance** as the writer.
  The repository uses that instance's connection. Real external reads prove that
  the domain receipt and SMB insert are invisible before commit, disappear on
  rollback, and appear together on commit. Rejected SMB insert and injected EF
  commit failure leave neither new receipt nor new SMB row.
* `IMessageBus.Publish(..., headers: ...)` preserves the proof's
  `StatisticsReceiptId` header (stable SampleId or EntryId).
  `headers ->> 'MessageName'` plus `headers ->> 'StatisticsReceiptId'` resolves
  exactly one retained SMB row with `SingleAsync()`; missing/ambiguous evidence
  cannot masquerade as success. This is **direct durable status observation**,
  not an inferred domain marker or broker acknowledgement. No stable default
  transport `MessageId` is claimed or needed by this proof.
* Actual constructed transport messages preserve topic `velotime-statistics`,
  transponder GUID `SessionId`, `MessageName`, UTC payload and metre distance.
  SampleComplete has no `Single`; Sample-derived EntryCreated has `Single=true`;
  derived EntryCreated has no `Single`. Accepted transport batches contain one
  event each.
* A controlled outage leaves work pending. Acceptance followed by an exception
  simulates a lost acknowledgement: `delivery_complete=false`, attempts recorded,
  `delivery_aborted=false`; after stop/start the same correlated payload is resent
  and eventually marked complete. Delivery remains **at least once**.
* A fresh scoped processing probe commits `EntriesCreatedAtUtc` **while the sender
  is still withholding acknowledgement**, and directly observes the matching SMB
  row's `delivery_complete=false`. Only after acknowledgement is released does
  SMB commit `delivery_complete=true`. Thus checking that flag in the live consumer
  and throwing when false is not a safe prerequisite for synchronous processing.
  The probe deliberately stands in for domain logic; it is not a new domain handler.
* In the **same database**, an independent bus using unchanged
  `AddStatisticsOutbox()` leaves a real pending API threshold row in
  `statistics.smb_outbox`. The processor delivers its three messages and the API
  row's complete persisted JSON remains unchanged. API tests below also pass.

The correlation header is test-only. A production writer must add it atomically,
only on the winning receipt insertion, preserve it across retries, validate the
typed payload, and enforce a unique retained mapping (for example a migration-owned
typed-header index or an explicit receipt-to-SMB-ID association). Do not guess
identity from payload similarity/timestamps or infer processing from delivery.
Raw SQL status reads couple the integration to SMB's schema and require version
compatibility tests and retained rows.

## Stock configuration is insufficient for processing order

`StockSequenceDoesNotWaitForPredecessorDomainProcessing` delivers two same-rider
messages while **both** receipt processing marks remain NULL.
`MaintainSequence=true` / batch size 1 is not the documented Statistics
per-transponder **processing** barrier.

Installed public API evidence:

```
IOutboxMessageRepository<T>.LockAndSelect(
    string, int, bool, TimeSpan, CancellationToken)
IOutboxMessageRepository<T>.UpdateToSent(IReadOnlyCollection<T>, CancellationToken)
IOutboxMessageRepository<T>.IncrementDeliveryAttempt(IReadOnlyCollection<T>, int, CancellationToken)
IOutboxMessageRepository<T>.AbortDelivery(IReadOnlyCollection<T>, CancellationToken)
```

There is no eligibility callback in that selection signature. `UseOutbox`'s
predicate receives `Type`, not the receipt/transponder or processing state.
`IPostgreSqlOutboxTemplate` does expose `LockAndSelect`, `LockTableAndSelect`
and `UpdateSent` SQL strings. A custom repository/template could change selection,
but that is a new custom eligibility implementation, **not a demonstrated stock
configuration switch**. Its concurrency, unrelated-rider progress, failure and
legacy gates would need separate proof.

## Durable-inbox alternative: bounded broker handling

`StatisticsSmbInboxBarrierProofTests.cs` is an executable **sample-only sketch**:

1. Validate an incoming typed payload against exactly one retained local receipt.
   Commit a deduplicated inbox reference **before** returning success. The retained
   immutable receipt supplies the payload in this sketch. Unverifiable/conflicting
   messages still fail explicitly, as do staging/commit failures.
2. Separately drain durable work in fresh transactions under the existing
   `statistics-transponder:{guid}` advisory lock. Wait for the exact SMB delivery
   mark and all lower local receipt IDs' processing marks. A blocked item stays
   parked durably; it does not consume broker delivery attempts.
3. Domain outputs, decisions, further SMB enqueues and completion marker must
   eventually commit in that same transaction. Polling is a restart-safe fallback
   for missed notifications/delivery marks. Failures retain work with backoff,
   monitoring and explicit repair/quarantine policy, not successful domain acknowledgement.

The test stages a reversed early successor and duplicate, rejects a conflicting
payload, then stages actual SMB deliveries before their marks. After stopping
the bus, the successor remains blocked; another rider progresses; failed predecessor
processing rolls back and does not release the barrier; retry then releases the
successor. No broker retry is used to wait for predecessor processing.

This is **not full integration**: it has no hosted drain, scheduling/backoff,
production inbox DDL, full outputs, Multi decisions or EntryCreated Single/version
routing. It preserves processing order in the demonstrated case, **not the existing
stronger publication barrier** (successors may already be in the broker).
Coordinator must approve that distinction, or require custom SMB eligibility.
Retain source receipts, local-order locking, no-result decisions and completion
marks from `SampleCompleteOutbox.md`, `EntryCreatedOutbox.md` and
`MultiCreationDecisions.md`; swapping senders cannot replace those semantics.
Increasing broker retries or suppressing ordering failures is not a solution.

## Reproduction and handoff (historical prerequisite)

Production conversion and current reproduction commands are now in
`StatisticsSmbProduction.md`. The retained fixture uses production outbox
registration/schema; the following is the original experiment's handoff.

Set `OIDC_AUDIENCE=https://build.invalid` and point
`VELOTIME_TEST_STATISTICS_POSTGRES` at an administrative connection on a
**disposable** PostgreSQL server. The API fixture instead uses
`VELOTIMER_PUBLIC_STATISTICS_POSTGRES`; it also creates isolated databases.
No connection string/password is checked in.

```
dotnet test src/VeloTime.Workers.Test --no-restore --filter FullyQualifiedName~StatisticsSmbOutboxProofTests
dotnet test src/Module.Statistics.Api.Test --no-build --no-restore --filter 'FullyQualifiedName~StatisticsThresholdPostgreSqlTests|FullyQualifiedName~StatisticsValidityThresholdsMessagingTests'
dotnet test src/VeloTime.Workers.Test --no-build --no-restore --filter 'FullyQualifiedName~StatisticsProcessorRegistrationTests|FullyQualifiedName~SampleOutbox|FullyQualifiedName~EntryOutbox|FullyQualifiedName~EntryDispatch|FullyQualifiedName~ExternalCompletionWithoutReceipt|FullyQualifiedName~ConcurrentReceiptInsertCannot|FullyQualifiedName~AmbiguousLocalCompletion'
```

Observed on real PostgreSQL: **6 proof tests**, **15 API tests** (9 PostgreSQL,
6 messaging), and **21 existing worker regressions** passed, zero skipped/failed.
The proof was repeated successfully. Existing dependency advisory warnings remain.
An initial API run used the wrong environment variable and skipped its 9 PostgreSQL
tests; the corrected run above executed all 15. No dependencies were installed.

Next owner: **Coordinator** for inbox-vs-publication-barrier decision and Data /
Platform prerequisites, then **Tester**. Remaining acceptance includes real handler
integration, empty-config/no-result Multi outcomes, missing/legacy/ambiguous receipts,
deletion/replay, concurrent replicas, cancellation, actual delivery-mark update failure,
crash after staging/before processing and before/after each commit, wake-up loss,
and bounded failure handling/monitoring. No Azure session settlement, lock renewal
or dead-letter behavior is claimed by this fake-sender experiment.
