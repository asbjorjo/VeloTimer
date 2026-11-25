# SampleComplete processor outbox

The stock SMB head-gate **schema prerequisite only** is documented in
`StatisticsProcessorSmbHead.md`. The dispatcher described below remains the
current implementation; the new protocol is not active until coordinated worker
and hosting conversion.

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
entry creation also sets that mark transactionally. IDs are allocated before their
transaction commits, but local receipt writers hold the same per-transponder lock
through commit, preserving committed local receipt order for each rider. Upstream
delivery order need not match event time, and independent producers/redrives do not
participate in that lock. A poison oldest row retries forever; one worker can block
other riders too. More replicas can claim unrelated riders.
Monitor error logs, pending count and oldest pending age, long transactions/locks and
table growth; do not casually delete or replay rows. A failed send is never marked
delivered. Retention and operator redrive policies require separate approval.

The consumer enforces the same boundary. TimingSampleHandler inserts the sample and
its immutable outbox receipt in a transaction holding the shared per-transponder
advisory lock. SampleCompleteHandler takes that lock before looking up its exact
local receipt, validating uniqueness and checking that all lower-ID local receipts
have committed entry creation. It marks only that receipt in the entry transaction.
Thus a concurrent local insert is either visible to the consumer after waiting for
the lock or the message fails without creating entries; a later local or replayed
message cannot pass an unprocessed committed predecessor. A missing or ambiguous
receipt fails explicitly and is not acknowledged as success. The handler does not
accept externally fabricated completions, and no global order is claimed for
uncommitted/future events or independent producers that do not use this lock.

Legitimate queued redeliveries remain supported when they match one retained local
receipt and its predecessors are complete. Before rollout, quarantine and reconcile
old queue, retry, in-flight and dead-letter messages against retained receipts,
Timing samples, broker state and downstream effects. A legacy queued message with no
unique retained receipt must remain quarantined until a separately approved
maintenance reconciliation restores trustworthy source/order evidence; event time
alone does not establish receipt order. Do not synthesize a receipt or redrive it
through the live handler when order cannot be established. If evidence is missing,
follow the manually coordinated reset/replay or loss procedure in `SampleIdentityPersistence.md`.
Old TimingSampleComplete events whose sample already exists but has no receipt also
require this reconciliation: the idempotent sample replay intentionally does not
fabricate a new receipt and now fails explicitly instead of acknowledging a missing
receipt as a successful replay.

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
