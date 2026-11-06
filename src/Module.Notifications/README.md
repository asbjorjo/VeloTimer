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
