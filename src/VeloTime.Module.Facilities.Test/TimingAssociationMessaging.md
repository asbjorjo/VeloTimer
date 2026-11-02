# Facilities timing-association publisher handoff

## API owner

Inject the scoped `VeloTime.Module.Facilities.Api.ITimingAssociationEventPublisher`:

```csharp
Task EnqueueAsync(CoursePointTimingAssociationsChangedV1 message,
    CancellationToken cancellationToken = default);
```

Use the **same DI scope's `FacilityDbContext`**, save mapping changes and enqueue while
its PostgreSQL EF transaction is active, then commit. The publisher does not save,
begin/commit a transaction, generate IDs, construct deltas, catch failures, or invalidate
caches. It rejects use without an active PostgreSQL transaction. On serialization,
outbox insertion, cancellation or deferred-constraint commit failure, roll back the
entire operation; never return success. Invalidate caches only after successful commit.
No-op detection and interval editing remain API responsibilities; do not enqueue no-ops.
See `TimingAssociationPersistence.md` for constraints, persisted precision and deployment
ordering. Construct snapshots at persisted PostgreSQL microsecond precision, not from
unrounded input; normalization here changes only offsets, not precision or interval semantics.

Generate non-empty `EventId` and `ChangeId` once for an atomic edit; preserve them across
delivery retries. The publisher copies deltas and normalizes occurrence/before/after
instants to offset-zero UTC without changing IDs, instants or null/unbounded bounds.
Use this abstraction, not direct `IMessageBus.Publish`, for this event.
Enqueue is persistence, **not** proof of broker receipt or downstream processing.
Endpoint handlers, interval business logic and Statistics consumers are not changed here.

`AddTimingAssociationMessaging` registers the Facilities-only producer and
`AddOutboxUsingDbContext<FacilityDbContext>` in schema `facility`. SlimMessageBus initializes
`facility.smb_outbox` itself: no EF entity or outbox migration is required. The application
DB role needs outbox schema/table initialization DDL privileges. Explicit hosted `bus.Start`
propagates schema initialization failures; a missing/blank `ConnectionStrings:MessageBus`
fails registration even in Development. OpenAPI's `GetDocument.Insider` is excluded, following
Timing's schema-generation pattern. Broker topology provisioning is disabled; startup
does **not** prove topic existence, permissions or broker availability. Send failures stay
pending with retry/error behavior, rather than becoming a success-shaped no-op.

## Delivery, topology and replay

* Facilities topic: **`velotime-facilities-test`** (stable constant, no Timing topic/subscription).
* Azure transport `MessageId = EventId.ToString()`; `SessionId = FacilityId.ToString()`
  groups atomic changes that may span coursepoints. JSON header
  `MessageName = CoursePointTimingAssociationsChangedV1`.
* At-least-once delivery; an accepted send followed by a lost acknowledgement can duplicate
  the original event. Persisted payload/IDs are stable; future consumers need durable
  `EventId` deduplication. `ChangeId` correlates the complete atomic edit.
* `MaintainSequence = true`, batch size 1, effectively unbounded attempts. This prevents
  later events overtaking a failed oldest event in the dispatch batch; it is **not**
  a global concurrent-commit/consumer ordering guarantee. A poison event can hold up later
  messages. Monitor pending count, age, attempts and dispatcher errors.
* No chart/AppHost topic, subscription or retention provisioning was found in the
  deployment assessment. No cloud resources were inspected or created. Before enabling
  this API, an external owner must precreate the topic, authorize the producer, and
  precreate a **durable future consumer subscription with a filter accepting the above
  `MessageName`** (for example, `MessageName = 'CoursePointTimingAssociationsChangedV1'`)
  and session-capable subscription/receiver configuration if session ordering is used.
  That owner must decide TTL, dead-letter/recovery, duplicate detection, session ordering,
  retention and replay policies. These are prerequisites, not resources this change provisions.
* Without a durable subscription **at publication time**, a successfully published topic
  event is not replayable from Azure. Topic publication does not create retained history.
  No exactly-once or guaranteed Azure replay claim is made.
* Dispatched outbox rows remain the explicit **DB replay source**: cleanup is disabled,
  unlike Timing's three-day policy. Pending and dispatched rows therefore grow without
  bound until an approved archival/retention plan replaces this policy. Back up the DB,
  monitor disk/index growth and protect against privileged deletion. Retention is not an
  automatic replay mechanism: no replay tool is added. A future controlled replay/export
  must preserve original payload/IDs, account for broker duplicate-detection windows and
  consumer deduplication, and must not blindly reset live dispatcher rows.

## Tests and remaining validation

The existing SQLite `FacilitiesApiFactory` supplies a dummy connection setting, removes
SMB/outbox hosted services and explicitly replaces `ITimingAssociationEventPublisher` with
`RecordingTimingAssociationPublisher`. `Events.Enqueued`, `Events.Failure` and `Events.Reset()`
support upcoming endpoint mapping/failure tests without a broker or SQLite outbox. This
test-only spy is **not transactional** and cannot prove rollback/durability; it is never a
production fallback.

Focused tests compile the actual API messaging sources (and test spy) as links in the domain
test project, avoiding known endpoint consumers of the removed mutable model. This validates
messaging compilation without claiming the full API or its factory builds/runs.

```sh
OIDC_AUDIENCE=https://build.invalid dotnet test src/VeloTime.Module.Facilities.Test/VeloTime.Module.Facilities.Test.csproj --configuration Release --filter 'FullyQualifiedName~TimingAssociationMessaging|FullyQualifiedName~TimingAssociationOutbox' --logger trx /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
OIDC_AUDIENCE=https://build.invalid dotnet test src/VeloTime.Module.Facilities.Test/VeloTime.Module.Facilities.Test.csproj --filter FullyQualifiedName~TimingAssociation --logger trx /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
OIDC_AUDIENCE=https://build.invalid dotnet build src/VeloTime.Module.Facilities.Test/VeloTime.Module.Facilities.Test.csproj --configuration Release
```

Validated in this worktree: focused Release run **7 passed, 7 skipped, 0 failed**;
broader association run **16 passed, 15 skipped, 0 failed**; Release build succeeded
with **0 errors, 6 existing CS0618 legacy `CoursePoint.TimingPoint` warnings**.
PostgreSQL configuration was absent, so real durability, migration-startup failure and
transaction-boundary tests are compiled but **not executed**. Full API/factory builds
were deliberately not run; the API owner's legacy model-consumer handoff remains open.

For real outbox checks, set `FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION` to a **test-only**
PostgreSQL server, `Database=postgres`, with create/drop database and `public.btree_gist`
installation privileges. The seven outbox tests each generate/migrate/drop a unique database;
they never use an existing application DB. SenderFactory supplies a controlled sender:
no live bus is required. Without that environment variable, the tests explicitly skip.

Tester: run these gates on PostgreSQL. Verify accepted-send/lost-ack duplicates retain
EventId/ChangeId/payload; outage/restart recovery across two hosts preserves tested order
and Facility session correlation; uncommitted edits/events are invisible; serialization,
outbox insert and deferred-exclusion commit failures roll back both; schema initialization
failure aborts startup. Then exercise the real API's displacement/split/unbounded-window
deltas and no-op suppression after the API owner implements them. Full API/factory checks
and external topology validation remain separate handoffs.
