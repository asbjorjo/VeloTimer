# Production Statistics stock SMB conversion

## Implementation and delivery boundary

`StatisticsHeadRelease` is an eligibility helper, not a dispatcher. The only
polling/sending task is stock SMB 3.4.0. Processor registration filters UseOutbox
to the two concrete event types, uses scoped StatisticsDbContext persistence in
`statistics_processor`, MaintainSequence, batch size one, one-second idle sleep,
int.MaxValue transport attempts and disabled cleanup. Startup validates the
prerequisite migration/state and explicitly starts the master bus/poller even
without inbound traffic. An unresolved stored type throws instead of SMB's silent
type-resolution abort. Undeclared inbound types no longer opt into successful ignore.

All three receipt-writing paths participate: TimingSampleHandler,
StatisticsOwnershipService and StatisticsService's internal Multi output creation.
Under the existing per-transponder advisory transaction lock, writers save all
receipts; consumers save all effects/completions, then the central helper releases
only the oldest unprocessed head in each independent stream. First SMB insertion
and SmbEnqueuedAtUtc commit with domain state. Completion is saved before marking
the successor to respect the partial unique indexes. No custom dispatcher,
startup sweeper, second sending path, global lock or approval flag exists.

Production consumers require an SMB context. They validate retained identity,
payload, version and headers before completed-duplicate handling, reject reversed
or unenqueued work, and do not revisit live definitions/owners on completed
redelivery. Non-Single entries commit an explicit no-recursion completion too.
Zero-result samples, empty Multi config sets and insufficient-parent decisions
are final. Direct domain replay retains its existing integrity checks; it is not
the broker duplicate path. No contract, migration, API outbox or topology change
was made. Existing EntryCreated topology already routes both Single forms.

## Executable evidence

`StatisticsSmbProductionTests.cs` adds **18 real-handler cases**, using production
registration, scoped EF/SMB transactions, actual PostgreSQL migrations, the real
Statistics/ownership/geometry/decision logic and a controlled Azure sender:

* Concurrent producer duplicates; reverse pending deliveries in both streams;
  concurrent/reversed late duplicates; identical measurement/source-independent
  IDs; independent Sample and Entry processing order.
* Processing commits while the first sender acknowledgement is held; successor
  remains unsent. Lost ack retries preserve identity and processing effects
  through complete provider/poller replacement against the retained database.
* SMB enqueue rejection and commit failure roll back sample/receipt/marker,
  Simple effects, Multi ledger/results/receipts and successor release. Cancellation
  at producer and consumer commit does the same.
* Real insert-first and empty-completion-first races prove actual PostgreSQL
  advisory-lock contention and exactly one successor enqueue, without wake-ups.
* Conflicting ownership rolls back multiple outputs/profile/receipts; concurrent
  repaired redrives assign once and complete all derived outputs.
* No definitions, empty Multi sets and no-result Multi decisions remain final
  after edits. Required-point failures and Facilities enrichment remain frozen
  during real transport retries and late configuration changes; retained domain
  fixtures also verify late-checkpoint replay and subsequent revalidation.
* An acknowledged, deliberately unconsumed Entry head blocks its own stream while
  Sample processing/another rider progresses. This is a fail-stop simulation,
  not actual Azure DLQ movement.
* Malformed bodies/headers/IDs still fail after completion. Deleted domain data
  and deliberate completed-SMB cleanup do not recreate outputs or enqueue mappings.
  Missing retained source evidence fails. Reconciled completed legacy redelivery
  does not fabricate an enqueue marker.
* Startup rejects unreleased/legacy/inconsistent or lost pending SMB associations;
  explicit startup starts an idle poller without inbound traffic.

The original **22 stock-SMB proof cases** remain and now use production outbox
registration/schema (only test poll/lease durations differ). They additionally
exercise delivery-mark SQL failures, pre-accept failures, finite transport aborts,
abort after processing/ack loss, explicit repair, provider restart and late copies.
The existing API-isolation case preserves a pending API SMB row byte-for-byte
while processor work is actually dispatched.

Obsolete direct-dispatch worker tests were removed, not kept behind fake
production dispatchers. Their retry, uncertain-ack, restart, mark failure,
uncommitted-publication, rollback, poison, empty-result, correlation and ordering
semantics move to these real-SMB cases and retained proofs. Two-replica direct-send
claim behavior is deliberately **not** claimed for this single-poller deployment.
The old non-Single-without-completion behavior is intentionally replaced by the
required explicit completion barrier. Historical Entry/Multi migration tests
remain in `StatisticsReceiptMigrationPostgreSqlTests.cs`; historical pending-
migration assertions now include the supplied schema prerequisite. Domain-only
fixtures use explicit test adapters/fake publication, not production envelope
bypasses. Their PostgreSQL locking, geometry, decision and replay checks remain.

## Production operation and limitations

The conservative stopped-writer maintenance requirements in
`../Module.Statistics/Storage/StatisticsProcessorSmbHead.md` are mandatory. Old
installations may be blocked because they lack authoritative completion/version
evidence. No reconciliation, schema reset, queue drain or shared database
migration was executed here. No old/new overlap or rolling multi-instance cutover.

Retain domain receipts/ledger indefinitely. Automatic SMB cleanup is disabled;
monitor database growth and define a separate completed-message retention policy
before enabling any cleanup. Never delete an unprocessed marked receipt's SMB
row. Startup checks that pending marked receipts retain exactly one matching
typed/header association; aborted rows are retained associations, not success.
The immutable enqueue marker survives completed SMB cleanup and is never cleared
or reused. Live arbitrary SQL/backfill writers and manual payload changes are
outside the protocol: restrict privileges.

Monitor/alert on pending receipt age, enqueued-but-unprocessed age, SMB
delivery_attempt/delivery_aborted, transport/type-resolution errors, broker
active/retry/dead-letter counts, and storage growth. Legacy DeliveredAtUtc is
historical only; do not use it as current completion or delivery health.

Recovery is explicit, not automatic:

1. For a delivered-but-unprocessed/poison/DLQ head, repair the underlying cause,
   verify retained identity/payload/Single/session, then redrive the **same**
   original message. Only committed processing releases its successor.
2. For transport failure, retain the original SMB row and repair connectivity or
   compatible type resolution. An aborted row requires reviewed operator repair
   of that row's retry state; do not create a replacement receipt/enqueue or fake
   delivery/completion. Tests repair rows deliberately; no operator tool is shipped.
3. Missing/ambiguous pending transport associations fail startup. Stop writers,
   inventory receipts, SMB/broker copies and downstream effects, and restore
   authoritative original state from retained evidence/backup. If that cannot be
   proven, stop; no safe automatic reconstruction is claimed.
4. Do not clear markers, delete heads, skip to successors, promote legacy versions,
   or synthesize effects to unblock. Prefer roll-forward; rollback requires
   stopped writers and reconciled database/broker backups, not only a code revert.

Single instance/poller is the premise. Domain locks remain rider-scoped, but one
persistently failing oldest **transport** row can starve unrelated riders with
the configured effectively-unbounded retries. First acceptance/committed domain
processing is causal; no global total order, exactly-once transport, or universal
“all predecessor duplicates precede successor” / predecessor delivery-mark rule
is promised. SMB uses timestamps, with no explicit tie-breaker; clock regression,
finite abort and late copies retain the limitations documented in the proof.

The sender is controlled: Azure settlement, actual sessions/DLQ/lock renewal,
network partitions, abrupt OS kill and PostgreSQL crash recovery were **not**
tested. Restart means disposing/rebuilding the full provider; crash windows are
transaction rejection or acknowledged-unprocessed simulations. HTTP ownership and
Facilities are controlled readers; Redis/OIDC/Azure are not integration targets.

## Reproduce / handoff

Set OIDC_AUDIENCE=https://build.invalid. Set VELOTIME_TEST_STATISTICS_POSTGRES to
an administrative connection on a disposable PostgreSQL server; API tests use
VELOTIMER_PUBLIC_STATISTICS_POSTGRES instead. Each case creates/drops its own
GUID database. No credentials are checked in.

```sh
dotnet test src/VeloTime.Workers.Test --no-restore --filter 'FullyQualifiedName~Statistics|FullyQualifiedName~WorkerHandlerTests|FullyQualifiedName~TimingSampleEventTimeTests'
dotnet test src/Module.Statistics.Api.Test --no-restore --filter 'FullyQualifiedName~StatisticsThresholdPostgreSqlTests|FullyQualifiedName~StatisticsValidityThresholdsMessagingTests'
```

Observed integrated run: **175/175 worker cases**, **15/15 API cases**, zero
failed/skipped. Includes the 18 production and 22 proof cases above. All **18**
production cases passed again after making startup's association validation
set-based rather than a correlated scan. Additionally:

```sh
dotnet test src/VeloTime.Module.Statistics.Test --no-build --no-restore
dotnet build src/Module.Statistics.Processor --configuration Release --no-restore
```

Domain tests: **89/89 passed**, zero skipped, with both PostgreSQL fixture variables
set. (Initial runs omitted one/both variables and skipped their database cases;
the corrected final run executed them all.) Release build: zero errors, six
existing dependency advisory warnings. NU1902/NU1903 warnings predate this change.
No full-solution build was run. Next owner: manual **Tester**, then read-only
review and the deployment operator for the mandatory cutover.
