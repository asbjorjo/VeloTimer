# Statistics entry replay identity and publication boundary

`StatisticsService` uses the caller's existing transaction and transponder advisory
lock (`StatisticsOwnershipService`) for both completion and parent-entry processing.
Do not call either creation method concurrently without that protocol. Item locks,
definition rediscovery after waiting, validity classification, ownership assignment
and dependent Multi scheduling still participate in that transaction. Multi creation
now reads live, untracked definitions under its verified lock scope, not cached or
previously tracked configuration objects.

## Identity and retained results

New entry primary keys are the first 16 bytes of SHA-256 over an invariant UTF-8 string.
The exact versioned encoding in `StatisticsService` is a persistence contract, not an
implementation detail to casually change:

- Simple: `statistics-completion-v1`, config ID, transponder ID, completing segment
  start/end ticks, and start/end **course** point IDs.
- Multi: `statistics-parent-v1`, child config ID, transponder ID, triggering parent
  statistics item/config IDs and triggering parent start/end ticks.

Fields are separated by `|`, GUIDs use `D`, integers use invariant decimal. Digest bytes
are passed directly to the .NET `Guid(ReadOnlySpan<byte>)` constructor. Domains have
separate prefixes. As with generated UUIDs, uniqueness assumes no 128-bit collision.
PostgreSQL timestamp normalization precedes hashing, querying and calculation; UTC
and base SI contracts are unchanged. No wire contract, schema or storage model changes
are made here.

The source completion is NOT just a finish, facility, selected attempt start, or a
current window. Different segment starts/points and different parent starts with equal
finishes remain distinct. Mutable distance, repetitions, required points, thresholds,
ownership and chosen contributors do not enter identity. Retries load the persisted
entry before deriving another measurement: late samples/parents and edited definitions
cannot change its original ID, endpoints, speed, facility or checkpoint outcome.
Current manual invalidation and ownership are not reset. Existing required-point and
course-layout readers retain their attempt semantics, geometry snapshots, fail-closed
checks and prohibition on course-layout Multi definitions. Only newly inserted Simple
entries reschedule dependent Multi validity; replays do not create extra contributors.
Later explicit validity revalidation still operates normally.

Replay lookup derives candidate keys from all retained configs of the relevant type
and uses the entry primary key, not a scan of historical entries. This also recovers
entries whose config has since moved to another endpoint/parent/item. Cost is linear
in definition count, not result history. Live routing still determines *new* results:
an initially empty result is not a durable processed-message receipt, and newly added
configs can create additional results on later delivery.

The current processors do not use SampleId/EntryId. If several persisted Timing source
edges collapse to one SampleComplete tuple, or several persisted parent entries have
the entire same EntryCreated tuple, creation fails explicitly rather than guessing
an identity or silently merging genuine attempts. Equal finishes alone are not
ambiguous. A parent statistics-item mismatch also fails explicitly. Duplicate
equivalent legacy sample evidence remains supported. Supporting
the ambiguous cases requires coordinated producer/consumer changes; retries alone
cannot resolve them. The additive identity contract and required rollout/alias
handling are specified in `../../Module.Statistics.Interface/Messages/StatisticsIdentityContract.md`;
the contract alone does not implement that handling.

## Legacy activation gate

Old random entry IDs cannot reliably be reconstructed from current measurements:
the selected attempt/window and configuration may have changed, and there is no
persisted source lineage. Do not guess deterministic IDs from entry endpoints or
adopt any row with a matching finish. Existing required-point/course-layout readers
can still return their legacy attempts, but that is NOT a general legacy migration.
Fixed and Multi history is included in the bounded preproduction reset implemented
by the activation migration described in `SampleIdentityPersistence.md`.
Do not run old random-ID writers alongside these writers. Rolling back reintroduces
duplicate creation, even though no schema change is necessary for these keys.

Preserve definitions, profiles, Timing and Facilities data and unrelated events.
The activation migration refuses any retained isolated outbox rows and restarts all
checkpoints; coordinate queued messages and do not clear shared schemas or outboxes.
No shared database has been migrated.

## Publication recovery and required worker handoff

**Implemented worker handoff:** see `EntryCreatedOutbox.md` for the transactional
publication boundary, isolated dispatcher, ordering barrier, migration and rollout
gate. The paragraphs below describe the prior retry-only boundary and why the
handoff was required; they are not the current handler behavior.

Both `SampleCompleteHandler` and `EntryCreatedHandler` commit via ownership before
publishing `EntryCreated` outside the transaction. Exceptions propagate. A redelivery
uses the persisted entries to reconstruct the same wire payload, including after a
partial batch send/uncertain acknowledgement; duplicate EntryCreated deliveries now
reuse existing Multi rows regardless of required-point settings. Multi configs have
Simple parents; their emitted EntryCreated messages do not recursively create Multis.
No other EntryCreated domain consumer was found in the repository.

This is **retry-safe**, not durable EntryCreated publication. Entry rows are not
publication receipts. If broker retries expire/dead-letter, the original message is
lost, or config/entry history is deleted before retry, there is no domain dispatcher
to finish publication. Worker/Messaging ownership must transactionally enqueue the
exact outgoing EntryCreated payload alongside entry persistence, including pending
payload recovery after routing/deletion changes, and dispatch it at least once with
existing routing/session semantics. Coordinate with Data & Migrations for its storage.
Do not infer sent status from row existence, suppress republishing, or mark the
end-to-end durable-publication acceptance criterion complete. Dispatcher activation
and historical replay remain gated on that handoff and legacy reconciliation.

## Regression acceptance for Tester

- Sequential and simultaneous completions/parent deliveries: one row per full source
  identity, including microsecond-normalized retries and separate equal-finish attempts.
- Publication failure after commit in each handler, fresh context/restart and repeated
  EntryCreated: original outgoing endpoints/config/item and one Multi row.
- Late start/checkpoint/parent evidence; changed distance/repetitions/requirements;
  config retarget/reparent; cached/tracked stale definitions; admin lock wait/reload.
- Required-point and course-layout outcomes, manual invalidation, ownership and
  revalidation unchanged; no replay-driven generation increment or extra contributors.
- Ambiguous source/parent tuples fail explicitly; authoritative legacy reconciliation
  or approved reset and worker durable publication need separate integrated validation.

PostgreSQL tests reuse the existing fixture which creates and drops its own randomly
named database; they do not drop the supplied connection's database or shared schema.
