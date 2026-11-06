# Notifications persistence primitives

`NotificationsDbContext` exclusively owns the `notifications` schema in `velotimedb`.
The migration worker applies only this context's migrations; EF history is kept in the
shared `ef.__ef_migrations_history` table, matching the other modules. There are no
cross-service references or source-domain inbox/consumer tables.

## Tables and invariants

- `notification` stores both localized plain-text variants, exact audience fields,
  active schedule, version, and the complete effective dismissal policy. Policy scope
  flags/default/duration are persisted on creation and database-checked as a complete
  valid policy. Dates are PostgreSQL `timestamptz` (UTC); duration remains SI seconds.
  The active interval is `starts_at_utc <= now AND
  (ends_at_utc IS NULL OR now < ends_at_utc)`. A finite end at or before the
  original start represents cancellation: that interval is empty at every time.
  There is deliberately no database start/end ordering check. Create/update enforce
  end > start when present; End preserves the original start and any earlier end,
  otherwise sets end and updated time to server UTC now and increments version.
  Finite timestamp checks remain enforced; no localized timestamps are stored.
  End atomically removes pending notice start/end slots and emits a refresh hint
  with the revision increment; repeating End with the current version is a no-op.
- `account_dismissal` has the unique primary key `(issuer, subject, notification_id)`
  and only duration/permanent state. `session_dismissal` has the unique primary key
  `(issuer, subject, notification_id, login_session_id)` and represents session scope.
  Every identity property uses PostgreSQL `C` collation: values are opaque, exact,
  case-sensitive strings. Each table also has a unique row `id`, used as the durable
  schedule source identifier. No anonymous dismissal is stored.
- `refresh_outbox` is one durable `NotificationsRefreshRequestedV1` event per
  `event_id`, which is also the transport MessageId. `claim_id` and `claimed_until_utc`
  form a lease; attempt count is persisted. Publication acknowledgement marks the row
  published and clears its lease. EventId uniqueness makes duplicate enqueue attempts
  idempotent.
- `scheduled_work` has one unique `(notification_id, kind, source_id)` slot. For start
  and end slots `source_id` is the notification UUID; for account/session expiry slots
  it is the corresponding dismissal row `id`. Rescheduling the same slot increments
  `generation`, replaces the due time, and clears completion and claim state; removing
  the source removes/cancels its slot in the same transaction. Workers lease due,
  incomplete rows using `claim_id`/`claimed_until_utc` and `attempt_count` in a short
  transaction using `FOR UPDATE SKIP LOCKED`, then commit that claim. In a separate
  processing transaction, lock the authoritative notice/dismissal source row first,
  then lock the slot row (the same lock order API mutations must use), and recheck
  claim ID, generation, current source state and exact due time. A worker atomically
  marks the still-current deadline complete and enqueues one refresh-outbox event.
  Stale generations, deleted/replaced dismissal IDs, and ended/edited notice state
  must be no-ops; never mutate the notice to make a deadline appear to have fired.
  Mutations that race the claim either invalidate its generation before processing
  or wait until the worker commits, then reschedule/cancel and enqueue their own
  refresh. Expired leases allow safe recovery after crash/restart.
- `snapshot_revision` contains a migration-seeded singleton `(id=1, revision=0)`.
  `SaveChanges` increments it atomically in the same transaction whenever tracked
  notification, account-dismissal or session-dismissal rows change. All bounded
  revision-checked snapshots can bind to this global revision. This deliberately
  invalidates more snapshots than strictly needed but avoids missing inserts behind a
  keyset cursor. Any future writer using raw SQL/`ExecuteUpdate` rather than tracked
  rows must increment this singleton in its own transaction. The token/API owner must
  bind the revision and UUID cursor to the authorized identity and reject a changed
  revision rather than silently skipping newly inserted rows.

Service mutations that create/change/end a notification or change/remove a dismissal
must commit the affected row(s), schedule upserts/cancellations, revision increment,
and refresh outbox insert together in one database transaction. Use unique-key
upserts (or retry after a unique-key conflict) for dismissal/outbox idempotency; an
identical effective dismissal retry must preserve its original dismissal time and
duration expiry. Notification `Version` is the optimistic concurrency fence for
admin edits. Scheduling and outbox claims use expiring leases plus claim IDs to fence
late acknowledgements. These are storage primitives only; this project does not
implement API services, endpoint authorization, outbox publishing, or deadline workers.

## Cancellation schema revision and deployment

The generated `notifications_allow_cancellation` corrective migration drops only
`ck_notification_schedule`. The initial migration is reported unapplied everywhere
known, but is retained unchanged to preserve its identity and support any database
that may already have it. Fresh databases apply both migrations in order; existing
databases apply only the correction. No columns, indexes, foreign keys, EF history
conventions or data are changed, and no backfill is required: every previously valid
row remains valid. Apply through the Notifications migrator before enabling the End
API; PostgreSQL `ALTER TABLE DROP CONSTRAINT` takes an exclusive table lock, so
schedule deployment around active transactions. The migration is transactional.

Down restores the old ordering check and will fail if cancelled rows (end <= start)
exist. Do not rewrite starts, delete notices or restore this constraint to force a
rollback; prefer a forward fix. Such rows are intentional state, not corrupt data.
Future deadline consumers must treat end <= start as never active and fence stale
activation slots using the authoritative end, as described above.

### Revision validation (2026-11-06)

- `dotnet build src/Module.Notifications/VeloTime.Module.Notifications.csproj
  --configuration Release` and the equivalent
  `src/Module.Notifications.Migration/VeloTime.Module.Notifications.Migration.csproj`
  build: both succeeded, zero warnings/errors.
- `dotnet ef migrations has-pending-model-changes --project
  src/Module.Notifications/VeloTime.Module.Notifications.csproj --context
  NotificationsDbContext --configuration Release --no-build`: no pending changes.
- `dotnet ef migrations script 20261106091958_notifications_initial` with the same
  project/context/configuration flags: transactional drop of only
  `ck_notification_schedule` plus the EF history insert. Full `migrations script
  --idempotent` also generated successfully. EF tooling 10.0.9 reports an older-tool
  warning against runtime 10.0.12; generation and checks succeeded.
- Applied initial then corrective migration using `dotnet ef database update`
  on a disposable loopback-only PostgreSQL 17 database, never a shared database.
  Initial schema reproduced future End failure with SQLSTATE `23514` and constraint
  `ck_notification_schedule`. Correction applied successfully with existing rows;
  both migration IDs were recorded in `ef.__ef_migrations_history`.
- Temporary external probe `/tmp/notifications-data-cancellation/Probe.csproj`
  (not committed tests), `dotnet run --project
  /tmp/notifications-data-cancellation/Probe.csproj --configuration Release`:
  **33 PostgreSQL checks passed** using the actual service and persisted rows.
  Covers future open/bounded notices, end exactly at start, active notice ending,
  unchanged start, exact UTC end/update time, version increment, empty-interval
  inactivity, start/end slot removal, revision/outbox increment, retry no-op,
  retention of an earlier end, create/update ordering rejection, and PostgreSQL
  rejection of infinite timestamps. Disposable database removed after validation.
  Tester should turn these scenarios into durable PostgreSQL integration tests;
  SQLite alone cannot validate the constraints, locks or transactional behavior.
