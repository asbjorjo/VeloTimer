# Statistics Lap distance rollout

## Stage 1: additive storage and legacy baseline

`20261118121122_statistics_lap_distance_storage` leaves all item modes Fixed (0).
Old writers still work: the database supplies Fixed when `distance_mode` is
omitted, and the new entry and sample columns are nullable. Install this schema
before deploying binaries that map those columns. No Facilities database access
is needed. `course_layout_id` is evidence from Facilities, not a local foreign key.

The transactional migration uses the exact legacy item name `Lap` (as used by
Bootstrap and Hall of Fame). If any of its entries need a snapshot, there must
be exactly one definition with a finite positive configured distance. Existing
foreign keys ensure entries have an item; a renamed item cannot be identified as
an old Lap by geometry or distance. Resolve missing/renamed definitions or
ambiguous names with operators before cutover; do not relabel records blindly.
No Lap entries needing a snapshot means no stage-1 backfill, including on an
empty database. No profile, validity, facility or moderation predicate is used.
Only rows with a null snapshot are updated, and only their distance and (for
positive persisted durations) speed. The `real` configured value is promoted to
`double precision` without decimal rounding. PostgreSQL `EXTRACT(EPOCH FROM
interval)` includes whole days and fractional seconds; the app stores finite
Npgsql `TimeSpan` intervals with no calendar months. An affected noncanonical
interval aborts rather than guessing. Nonpositive durations retain their speed.

DDL and the backfill commit together. PostgreSQL holds its ALTER TABLE locks
through the update: plan a low-traffic window and allow for write-ahead log,
row/index rewrites (including the speed index), replication lag and blocked
readers/writers; check legacy definitions and affected row counts first. Stage-1
Down drops the new columns and cannot restore previously stored speeds. Prefer
rolling back to the old binaries while leaving the additive schema installed.

## Stage 2: final backfill and activation (not shipped here)

The integration owner should add a distinct EF migration **only after** the
Facilities producer and all Statistics writers/readers, definition mapping and
validation, UI and Bootstrap changes are integrated and verified. The Statistics
migration host applies all pending migrations automatically: do not ship or run
the activation migration as part of an ordinary rolling upgrade with old
binaries still running. In the interim, keep Lap Fixed, freeze its name and
configured distance, and do not allow early CourseLayout activation. Readers
must not assume all Fixed Lap rows are snapshotted while old writers can still
insert nulls after stage 1. Stage 1 intentionally does not close that gap.

1. Deploy Facilities evidence production and the evidence-persisting Sample
   writer on the additive schema. Verify evidence on new samples. Plan the queue
   boundary: drain old completions while Fixed and retain/replay messages only
   as supported by the integrated runtime. In-flight attempts with pre-evidence
   samples must not be assigned a fallback distance after activation; handle and
   diagnose them as unresolved, or replay with appropriate authoritative evidence
   where available. Do not silently drop backlog or remeasure existing results.
2. Stop and drain all Lap entry writers and block definition/Bootstrap mutations;
   take old readers offline before enabling the zero sentinel. In the activation
   migration take transaction-held locks on `statistics.statistics_item` and
   `statistics.statistics_entry` (e.g. `SHARE ROW EXCLUSIVE`, in that order) to
   exclude concurrent DML during validation and update. The locks do not replace
   quiescing legacy processes: a waiting old writer could resume after commit.
3. Call `migrationBuilder.Sql(statistics_lap_distance_storage.BackfillLegacyLapDistancesSql)`
   without transaction suppression. Keep the referenced SQL frozen. In the same
   transaction, validate the activation target even if the helper had no null
   rows: if Lap exists, require exactly one intended legacy definition, a valid
   Fixed baseline, and a supported configuration (including no Multi usage or
   dependencies). Reject ambiguity, an invalid baseline or unsupported config
   rather than activating arbitrarily. Confirm there are no remaining null Lap
   snapshots, then update that same item ID to `distance_mode = 1`, `distance = 0`
   without changing its duration bounds or configuration IDs. With no Lap on a
   fresh database, activation is a no-op; updated Bootstrap creates it directly
   in CourseLayout mode. Verify the migration is safe both for upgrades with a
   gap between stages and for fresh installs where both run before Bootstrap.
4. Check the migration history, definition and snapshot coverage before resuming
   upgraded processes. A failed activation must roll back both the final sweep
   and the mode switch. Do not restart old binaries against an activated item;
   plan recovery before cutover (the old configured distance is overwritten by
   the sentinel, and new layout-derived snapshots cannot be converted back to a
   single configured distance).

Tester: cover non-250 configured distances, private/unlinked/invalid entries,
positive durations with whole days and fractional seconds, zero/negative duration,
untouched existing snapshots and non-Lap rows, empty databases, affected ambiguous
or invalid definitions with transactional rollback, and rows written between stages.
