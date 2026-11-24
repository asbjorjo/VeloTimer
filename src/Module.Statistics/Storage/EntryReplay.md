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

- Simple: `statistics-completion-v2`, simple config ID and persisted Statistics
  `Sample.Id`.
- Multi: `statistics-parent-v2`, multi config ID and persisted triggering
  `StatisticsEntry.Id`.

Fields are separated by `|`, GUIDs use `D`, integers use invariant decimal. Digest bytes
are passed directly to the .NET `Guid(ReadOnlySpan<byte>)` constructor. Domains have
separate prefixes. As with generated UUIDs, uniqueness assumes no 128-bit collision.
PostgreSQL timestamp normalization precedes source validation, querying and calculation;
UTC and base SI contracts are unchanged. No wire contract, schema or storage model
changes are made here.

The identity is the persisted source, not a finish, facility, selected attempt start,
or current window. Distinct physical samples or parent entries with identical old
payloads therefore derive independent rows. `ProcessCompletedAsync` accepts an optional
sample ID and source distance after the existing cancellation argument; when an ID is
provided it must be non-empty, exist, and match the persisted transponder, normalized
endpoints, course points and distance. It never falls back to tuple matching. An
ID-bearing completion must include a finite source distance. An ID-less completion
resolves by the persisted completion payload (including distance when the caller
supplies it) only if one source is authoritative; missing and multiple candidates fail
explicitly. `ProcessEntryAsync` similarly accepts an optional parent EntryId and, when
provided, validates its transponder, item, config and normalized endpoints. ID-less
parent events require exactly one matching persisted parent and matching item/config.
Once resolved, ID-less and ID-bearing deliveries use the same source key and output ID.

Mutable distance/configuration settings, repetitions, required points, thresholds,
ownership and chosen contributors do not enter identity. Retries load the persisted
entry before deriving another measurement: late samples/parents and edited definitions
cannot change its original ID, endpoints, speed, facility or checkpoint outcome.
Current manual invalidation and ownership are not reset. Existing required-point and
course-layout readers retain their attempt semantics, geometry snapshots, fail-closed
checks and prohibition on course-layout Multi definitions. Only newly inserted Simple
entries reschedule dependent Multi validity; replays do not create extra contributors.
Later explicit validity revalidation still operates normally.

Replay lookup derives source keys from retained configs of the relevant type and
queries entry primary keys, not result history. It also recovers entries whose config
has since moved to another endpoint/parent/item. Cost is linear in definition count,
not result history. The required-point and course-layout readers no longer use
finish/facility tuple matches as a replay shortcut; the source-derived key is the
authority, so two sources at the same finish cannot return each other's measurements.
Live routing still determines *new* results: an initially empty result is not a durable
processed-message receipt, and newly added configs can create additional results on a
later delivery.

The additive identity contract is in
`../../Module.Statistics.Interface/Messages/StatisticsIdentityContract.md`.
Handlers and dispatchers propagate `SampleComplete.SampleId`, completion distance,
and `EntryCreated.EntryId`, preserving transactions, receipt barriers, `Single`
and routing. Multi decisions, including an empty eligible set and no-result
outcomes, are durable and frozen on replay; see `MultiCreationDecisions.md`.
Simple completion routing can still discover newly added definitions on redelivery.

## Legacy activation gate

The former `statistics-completion-v1` and `statistics-parent-v1` tuple keys are
incompatible with the source-keyed v2 identities. The domain checks the corresponding
v1 key before replay and throws a maintenance-reconciliation error if it exists. It
does not silently adopt a v1 row, assign it to one of multiple sources, or create a
second v2 row beside it. There is no persisted v1-to-source association, and even a
currently unique tuple is not proof of which source originally produced a v1 entry.
No automatic bridge is safe without new persistent lineage and an approved data
migration; none is introduced here.

The preproduction activation migration described in `SampleIdentityPersistence.md`
deletes all derived samples and entries before source-identity activation. Verify that
the reset barrier was actually applied and that no tuple-v1 entries were subsequently
retained before rollout. If any might remain, stop and use an explicitly approved
reconciliation/reset procedure; the runtime guard is defense in depth, not a history
inventory and not a substitute for the migration barrier. Preserve definitions,
profiles, Timing and Facilities data and unrelated events. The activation migration
refuses retained isolated outbox rows and restarts all checkpoints; coordinate queued
messages and do not clear shared schemas or outboxes. No shared database has been
migrated.

## Durable publication and recovery

Both handlers atomically enqueue exact `EntryCreated` payloads with their entry and
ownership changes. Isolated dispatchers retry retained payloads at least once;
uncertain acknowledgement may duplicate delivery, not domain rows or decisions.
See `EntryCreatedOutbox.md` and `MultiCreationDecisions.md` for receipt ordering,
zero-result decisions, validation, migration and the mandatory maintenance barrier.
A validated non-Single event has no dependent Multi work and is acknowledged without
recursive creation. Legacy or unprovable messages require explicit reconciliation.

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
