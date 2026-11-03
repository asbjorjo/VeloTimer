# Statistics transactional outbox handoff

`AddStatisticsOutbox(this MessageBusBuilder)` is a storage registration hook for the
API owner's configured SlimMessageBus bus. It uses centrally versioned
`SlimMessageBus.Host.Outbox.PostgreSql.DbContext` 3.4.0, the same provider and
transaction pattern as Timing/Facilities. No per-project version or tool-manifest
change was needed.

The hook calls UseOutbox and AddOutboxUsingDbContext<StatisticsDbContext>, with:

* PostgreSQL schema `statistics`; runtime-owned `statistics.smb_outbox`;
* MaintainSequence=true, poll batch size 1, idle sleep 5 seconds;
* MaxDeliveryAttempts=int.MaxValue;
* cleanup disabled pending an approved retention/replay policy.

SlimMessageBus initializes its own tables when the bus starts. There is deliberately
**no EF outbox entity/snapshot/table migration**. Outbox schema/table/index initialization
requires application-role DDL privileges or an approved provisioning arrangement.
The migration worker does not start this bus and does not initialize the outbox.

## API/messaging host responsibility

Configure the Statistics-only producer for `StatisticsValidityThresholdsChangedV1`,
transport/JSON serializer and MessageName header, then call AddStatisticsOutbox on
that builder. Producer MessageId should preserve EventId; item is the natural session/
ordering scope (including config edits). Topic/subscription/filter/session behavior
must be agreed with Workers/platform, not silently invented by storage.

Explicit hosted `IMasterMessageBus.Start()` must propagate outbox initialization
failures to host startup, following Timing/Facilities prior art; logging and continuing
is not sufficient. Check missing MessageBus configuration, topology prerequisites,
credentials/permissions and the repository OpenAPI-generation startup convention.
This hook registers persistence only: it does not register a producer, hosted startup,
API event publisher, broker topology or consumer.

The API owner must provide a guarded enqueue abstraction requiring an active
**PostgreSQL StatisticsDbContext transaction**, preserving nonempty EventId and scope.
Use the same DI scope/context/connection for definition SaveChanges, durable checkpoint
scheduling and bus.Publish, then commit. Any serialization, insert, cancellation or
commit failure must roll back all three; invalidate caches only after successful commit.
Do not save definitions then publish outside their transaction. Enqueue proves
persistence, not broker receipt or historical completion.

The current processor already has its own bus/producers; do not blindly enable outbox
for it without reviewing all existing publishing transactions. The chosen checkpoint
worker polls Statistics-owned durable pending rows and needs no new broker continuation
contract. Threshold events are wakeups; dependency scheduling/checkpoints can remain
local transactional writes. See ValidityPersistence.md for the required writer locks,
generation handling, keysets, bootstrap and exact lineage protocol.

## Delivery and deployment

At least once, not exactly once. Duplicate/out-of-order events wake the durable current
job; they must not increment its generation, reset its checkpoint or replay old settings.
MaintainSequence/batch size 1 is dispatch behavior, **not** global concurrent-commit
ordering. A poison oldest event can obstruct dispatch; monitor pending age, errors,
attempts and disk growth. Unbounded attempts do not replace dead-letter/operator recovery.

Platform must precreate a durable Statistics subscription/filter accepting
`MessageName = 'StatisticsValidityThresholdsChangedV1'` before publication, agree any
session ordering, and authorize the sender/receiver. No cloud resources were inspected
or provisioned. Stored pending job polling permits revalidation even after a lost wakeup,
but is not a substitute for other subscribers' delivery requirements.

Cleanup-disabled pending/dispatched rows grow until retention/archival/replay is approved.
Retained rows are a possible explicit database replay source, not an automatic replay
mechanism; no replay tool is implemented. Do not reset live dispatcher records blindly.

## Executed checks / remaining owner

The core and migration-runner Release builds pass (0 warnings/errors), using the
new package reference and actual storage extension. A temporary external console
harness, `/tmp/velotime-statistics-storage-validation/StorageValidation.csproj`,
ran against a dedicated disposable PostgreSQL 17 database with a controlled unavailable
ServiceBusSender, **no Azure contact**:

```sh
OIDC_AUDIENCE=https://build.invalid dotnet run \
  --project /tmp/velotime-statistics-storage-validation/StorageValidation.csproj \
  --configuration Release
```

The full harness passed **24 assertions**, including storage/lineage/checkpoint checks
described in ValidityPersistence.md plus real Statistics outbox initialization,
uncommitted event invisibility on another connection, atomic definition/checkpoint/event
rollback, atomic commit, and an unavailable sender leaving the event pending/durable.
It stopped the bus on completion. A follow-up `-- --new-entries` run passed **2 assertions**
for explicit new-row false validity despite the true default, retained negative duration/
speed, and compatible omitted validity/lineage defaults. Repeat EF database update
reported already up to date. No shared database was migrated.

This temporary harness is evidence, not maintained regression coverage or production
host wiring. Tester must add/run repository PostgreSQL tests for serialization/outbox
insert/commit failures, startup DDL failure, accepted-send/lost-ack duplicate delivery,
outage/restart/two-host recovery, replay/retention, and the real API scheduling transaction.
API/Workers/platform hosting and topology remain next-owner work through Coordinator.
