# Statistics completion and entry identities

This is a contract-first change only. Current dispatchers and consumers do **not**
yet send or use these fields; the ambiguous-tuple bug is not fixed by this change.

`SampleComplete.SampleId` is the persisted **Statistics** `Sample.Id`, not a
Timing sample/point ID, message ID, transponder ID, or outbox sequence. The
`SampleCompleteOutbox.SampleId` is its immutable source. `EntryCreated.EntryId`
is the persisted `StatisticsEntry.Id`, not its configuration or item ID;
`EntryCreatedOutbox.EntryId` is its immutable source. Both are nullable only for
compatibility: new publishers must send the stored non-empty GUID on every
attempt, including retries. Null or omitted means legacy/unknown, never
`Guid.Empty`; a supplied empty GUID is invalid. No new aliases or alternate JSON
field names are defined. Times remain UTC, distance in metres. Existing message
names, topic, `Single` header and transponder session routing are unchanged.

## Required integration (not implemented here)

* Update both isolated outbox dispatchers to send the respective retained IDs;
  never generate an ID during dispatch. Preserve immutable payload, ordering,
  cancellation, transaction, retry and acknowledgement boundaries.
* Under the existing transponder lock, completion handling must select the
  receipt by SampleId, validate the transponder and **all** stored payload
  fields using existing timestamp precision, then enforce the existing earlier
  receipt barrier. Reject missing IDs in storage, empty IDs, or mismatches:
  no tuple fallback when an ID was supplied. Use that exact sample for source,
  facility and attempt-evidence selection. Preserve fail-closed evidence rules.
* Entry handling must resolve EntryId to its matching persisted parent/retained
  receipt, validate transponder, item, config and normalized endpoints; reject
  missing/conflicting sources. Use the actual parent in downstream processing.
  Do not reinterpret `Single`, reorder deliveries, or acknowledge failures.
* Replace tuple-only creation identities with distinct, versioned domains for
  `(simple config ID, SampleId)` and `(multi config ID, triggering EntryId)`.
  Persist/retrieve the same result on replay, without conflating different
  sources with equal payloads. Version the encoding and consider hash collisions
  and existing replay/attempt readers, not just receipt selection. Preserve
  transactions, validity, ownership and dependent scheduling. Two IDs are
  necessary: distinct Simple results can themselves produce identical parent
  payloads.

## Legacy messages, aliases and rollout

An ID-less completion may use the existing complete payload to locate a receipt
only when **exactly one** matching receipt is authoritative; enforce the same
ordering barrier. An ID-less entry may use its complete parent tuple only when
exactly one authoritative parent exists and item/config agree. Zero, multiple,
conflicting, deleted or otherwise unprovable matches require failure and
maintenance reconciliation, not arrival-order, timestamp, facility or arbitrary
row guesses. This applies to old queued, delayed and dead-letter events too.
Once uniquely resolved, ID-less and ID-bearing deliveries for that source must
be aliases of the *same* processing identity and outgoing EntryId.

The existing `statistics-completion-v1` and `statistics-parent-v1` keys are
tuple-derived. Blindly changing to new keys duplicates historical entries;
blindly adopting a v1 key for one of several sources merges real attempts.
The implementation must bridge a v1 key only when authoritative retained
evidence proves its unique source and payload; otherwise stop for explicit
reconciliation. In particular a tuple that was formerly unique but is now
ambiguous cannot safely assign its v1 result to either source by guessing.
Persist the proven association before accepting later distinct sources; a
legacy tuple-derived entry alone is not proof of which source created it.
Persist any necessary alias/lineage atomically or coordinate a Data & Migrations
design, with a plan for missing/deleted evidence and retries. The existing
`Storage/EntryReplay.md` and `Storage/SampleIdentityPersistence.md` activation
and reset barriers remain in force; this contract does not authorize another
reset, fabricate lineage, or guarantee historical regeneration.

Old default System.Text.Json/SlimMessageBus readers ignore the additive fields, but
still apply tuple-only logic: wire compatibility is **not** semantic mixed-writer
safety. External readers with strict unknown-member handling may reject them.
Inventory external consumers; deploy identity-aware consumers and
alias/reconciliation support before identity-bearing publishers, and prevent old
tuple-only processors or writers from running alongside the new processing.
Drain/quarantine and reconcile in-flight, retry and dead-letter messages and
retained outboxes at the maintenance barrier where safe aliasing cannot be proven;
do not infer downstream success from a broker or outbox delivery mark. Preserve
per-transponder order during any approved redrive.

Tester: cover duplicate and concurrent deliveries, two Timing identities with
identical completion payloads, identical parent payloads with distinct EntryIds,
legacy/new aliases in both orders, v1 ambiguity, missing/mismatched IDs and
deleted evidence, ordering barriers, rollback and uncertain publish outcomes,
restart/retry, and `Single`/MessageName/session correlation.
