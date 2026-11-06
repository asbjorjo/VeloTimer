# Notifications Processor

This worker owns only durable Notifications refresh-hint publication and scheduled
activation/end/account-duration-expiry wake-ups. It has no source-domain consumers,
inbox, notification-content payloads, or session-expiry lifecycle integration.
Platform & Deployment owns solution/AppHost/Helm/CI registration.

## Runtime contracts

- Database connection: `ConnectionStrings:velotimedb`, using the core module's
  `NotificationsDbContext` and the existing Notifications schema/migrations. Apply
  both Notifications migrations before starting this worker.
- Broker connection: `ConnectionStrings:MessageBus`. Missing configuration fails
  startup. The broker topic is `velotime-notifications`; events use the shared
  `NotificationsRefreshRequestedV1(Guid EventId)` contract, `MessageName` header,
  and `MessageId = EventId`. No session ID or message content is added.
- Broker topology provisioning is disabled. Platform owns topic/subscription/filter
  provisioning and must create the logical `webui-notifications` subscription with
  `MessageName = 'NotificationsRefreshRequestedV1'`, non-session. The competing
  WebUI bridge subscription requires its separate SignalR Redis backplane for
  multi-replica delivery.
- Worker tuning is under `Notifications:Worker` (environment nesting uses `__`):
  `BatchSize` (default 50, 1..500), `PollIntervalSeconds` (0.25, 0.25..60,
  fractional seconds supported; existing integer values still bind),
  `ClaimLeaseSeconds` (60, 5..3600), `RetryInitialDelaySeconds` (2, 1..300), and
  `RetryMaximumDelaySeconds` (300, 1..3600, at least the initial delay). Defaults
  are appropriate for local runtime; deployment should size leases above normal
  processing latency and poll/retry intervals to its delivery objective.

Each cycle processes one bounded due-deadline batch **before** one bounded outbox
batch. Atomically committed deadline hints are eligible for publication in that
same cycle, subject to ready-outbox ordering and batch capacity. The hosted worker
then waits the configured poll interval with cancellable `Task.Delay`; it does
not busy-wait, overlap cycles, or drain indefinitely under load. Idle work becoming
eligible just after a cycle normally waits up to 250 ms plus database/publication
time at defaults, leaving headroom for the local <= 2-second client-callback target.
Backlogs, slow publication/source locks, retries and lease recovery can exceed
that target; this is not a hard end-to-end SLA.
Budget for four idle cycles per second per replica (two short claim transactions
per cycle), rather than the previous half-cycle per second.

Platform & Deployment must default `Notifications__Worker__PollIntervalSeconds`
to `0.25` wherever a poll override is supplied (or omit it to inherit this default).
An explicit legacy `2` remains valid but consumes the entire local delivery budget
before broker/backplane/browser latency. Keep the same seconds-based key; do not
substitute milliseconds. Platform owns Helm/AppHost/deployment configuration.

## Delivery and recovery

The outbox dispatcher claims ready unpublished rows in a short transaction with
`FOR UPDATE SKIP LOCKED`, persists a unique claim ID/lease and incremented attempt,
then commits before calling the broker. Successful publication is acknowledged only
when the same claim ID still owns the event. A publish failure is logged and stores
a capped exponential retry time; a failed retry write leaves the lease to expire.
After a process crash, the expired lease makes the same stable EventId eligible
again. Broker acceptance followed by a lost database acknowledgement can therefore
produce a duplicate event: delivery is intentionally at-least-once. There is no
success-shaped fallback, bounded-attempt discard, or direct-send bypass. Retry delay
is bounded, while durable retries continue until acknowledgement.

Deadline claims also use a brief `SKIP LOCKED` claim transaction. Processing starts
a separate transaction and locks in API-compatible order: notification source,
account/session dismissal source when applicable, scheduled-work slot. The worker
rechecks claim ID, generation, slot/source IDs, exact UTC due time, and authoritative
source state before completing. It does not mutate notices or dismissals. A valid
deadline completion, global snapshot-revision increment, and new refresh outbox row
commit atomically; revision fencing is required because temporal eligibility can
change without a tracked model update. Stale generations, deleted/replaced sources,
and intervals with `end <= start` complete without emitting a hint. Errors are
logged and the claim lease is allowed to expire for recovery.

Only current `NotificationStart`, `NotificationEnd`, and account duration expiry
deadlines emit refresh hints. Session-expiry work is not authored absent a trusted
application-login lifecycle feed; an unexpected such slot is warned and completed
without inferring session expiry or publishing a misleading refresh.

## Checks and non-claims

Build:

```sh
OIDC_AUDIENCE=https://build.invalid dotnet build \
  src/Module.Notifications.Processor/VeloTime.Module.Notifications.Processor.csproj \
  --configuration Release
```

PostgreSQL worker-specific tests are in
`src/Module.Notifications.Processor.Test`. They create uniquely named databases,
apply the real Notifications migrations, and drop only those databases. Point the
environment variable `NOTIFICATIONS_WORKER_TEST_ADMIN_CONNECTION` at a disposable
PostgreSQL admin database to run them:

```sh
NOTIFICATIONS_WORKER_TEST_ADMIN_CONNECTION='Host=127.0.0.1;Database=postgres;...' \
  dotnet test src/Module.Notifications.Processor.Test/VeloTime.Module.Notifications.Processor.Test.csproj \
  --configuration Release
```

These tests exercise durable claim/retry/restart, duplicate-safe stable EventIds,
deadline completion/revision/outbox atomicity, and the empty-interval cancellation
guard, including same-cycle deadline publication, failed deadline-hint publication
without reprocessing the deadline, and bounded batches. Configuration tests verify
fractional/legacy poll values and reject invalid or non-finite intervals.
Real hosted-loop tests use PostgreSQL and wall-clock timers to make outbox
and deadline work due just after an idle cycle, assert fake-publication within two
seconds at defaults, and exercise cancellation/acknowledgement. They use a publisher
test double: they do not prove Azure Service Bus
availability, broker topology, or end-to-end WebUI SignalR delivery.
