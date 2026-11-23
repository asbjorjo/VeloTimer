# SampleComplete processor outbox

The processor uses the EF-owned `statistics.sample_complete_outbox`, not the API's
`statistics.smb_outbox` or SlimMessageBus's schema-wide sequence. Only the sample
handler writes it. EntryCreated uses its own transactional processor outbox; see
`EntryCreatedOutbox.md`. The unchanged Timing completion contains only the source identity:
transponder, original Timing endpoints, and endpoint instants. Normalize instants to UTC
and PostgreSQL precision before lookup, Facilities enrichment and insert. There are no
additional immutable input fields to compare. The unique keyed sample index arbitrates
races; only the winner inserts the immutable completion payload in the same transaction.
A replay uses the stored sample without consulting Facilities or adding an outbox row.

The processor hosted worker claims a pending row using PostgreSQL `FOR UPDATE SKIP
LOCKED`, holds the transaction across direct bus send, and marks it delivered only
after acknowledgement. An uncertain acknowledgement, process death, or failed commit
can resend; consumers MUST be idempotent. No exactly-once transport is promised.
The existing producer supplies `MessageName`, topic and transponder `SessionId`.
Rows are retained indefinitely. The partial index and claim rule prevent dispatching
a later *visible* row for a transponder while an earlier one is pending, locked, or
has not committed entry creation (`entries_created_at_utc IS NULL`). Zero-output
entry creation also sets that mark transactionally. Sequence allocation is not commit ordering: concurrent uncommitted inserts,
upstream deliveries and retries can still reorder. A poison oldest row retries forever;
one worker can block other riders too. More replicas can claim unrelated riders.
Monitor error logs, pending count and oldest pending age, long transactions/locks and
table growth; do not casually delete or replay rows. A failed send is never marked
delivered. Retention and operator redrive policies require separate approval.

Apply the new migration before deploying processor code, and ensure the processor role
has DML rights on the table and identity sequence. Down refuses while any retained
rows exist. Stop workers and reconcile delivery/backup before a rollback: restoring
the old handler loses atomicity and duplicates become possible. This migration does
not reset or reconcile legacy samples, entries, queues, or the API outbox. The
activation/reset prerequisites in `SampleIdentityPersistence.md` apply. Historical
unkeyed rows are not made safe by the completion outbox migration alone.

**Deployment gate:** domain fixed/multi replay identity is documented in `EntryReplay.md`.
Durable `EntryCreated` publication is implemented in `EntryCreatedOutbox.md`, including
mandatory handling of retained delivered-but-unmarked SampleComplete rows at upgrade.
Legacy entry identity still requires reconciliation/reset before activation or replay.
Coordinate in-flight messages and API threshold outbox independently; do not clear
`statistics.smb_outbox`. The payload has no event ID beyond its source identity;
dispatched rows and broker acknowledgements do not establish downstream success.
