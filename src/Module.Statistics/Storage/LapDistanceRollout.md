# Statistics Lap distance rollout

## Stage 1: additive storage and legacy baseline

`20261118121122_statistics_lap_distance_storage` leaves all item modes Fixed (0). Old writers still work: the database supplies Fixed when `distance_mode` is omitted, and the new entry and sample columns are nullable. Install this schema before deploying binaries that map those columns. No Facilities database access is needed. `course_layout_id` is evidence from Facilities, not a local foreign key.

The transactional migration uses the exact legacy item name `Lap` (as used by Bootstrap and Hall of Fame). If any of its entries need a snapshot, there must be exactly one definition with a finite positive configured distance. Existing foreign keys ensure entries have an item; a renamed item cannot be identified as an old Lap by geometry or distance. Resolve missing/renamed definitions or ambiguous names with operators before cutover; do not relabel records blindly. No Lap entries needing a snapshot means no stage-1 backfill, including on an empty database. No profile, validity, facility or moderation predicate is used. Only rows with a null snapshot are updated, and only their distance and (for positive persisted durations) speed. The `real` configured value is promoted to `double precision` without decimal rounding. PostgreSQL `EXTRACT(EPOCH FROM interval)` includes whole days and fractional seconds; the app stores finite Npgsql `TimeSpan` intervals with no calendar months. An affected noncanonical interval aborts rather than guessing. Nonpositive durations retain their speed.

DDL and the backfill commit together. PostgreSQL holds its ALTER TABLE locks through the update: plan a low-traffic window and allow for write-ahead log, row/index rewrites (including the speed index), replication lag and blocked readers/writers; check legacy definitions and affected row counts first. Stage-1 Down drops the new columns and cannot restore previously stored speeds. Prefer rolling back to the old binaries while leaving the additive schema installed.

## Stage 2: final backfill and activation

`20261118125831_statistics_lap_distance_activation` is a distinct, transactional Statistics migration. Its EF `Up` locks `statistics.statistics_item` and then `statistics.statistics_entry` in `SHARE ROW EXCLUSIVE` mode, checks a PostgreSQL session setting for existing Lap definitions, reuses the frozen stage-one SQL to backfill null snapshots, validates the target, then updates only that Lap row to `distance_mode = 1` and `distance = 0`. Definition and configuration IDs, duration limits, entries, identity and moderation state are preserved. A missing Lap definition is a fresh-install no-op; the updated Bootstrap seed creates Lap directly in CourseLayout mode. The 250m pursuit item remains Fixed and unchanged.

The migration requires exactly one item named `Lap`, a Fixed finite positive legacy distance, at least one unique Simple same-point loop config with nonzero coursepoint IDs, no non-Simple config for Lap, no Multi config using a Lap config as its parent, and every Lap entry has a positive finite snapshot. Failure aborts the transaction, including the final sweep and activation. Existing Lap installations also require `current_setting(velotime.lap_distance_activation, true) = on`; without it, an ordinary Helm pre-upgrade fails before changing data or deploying the new release. No acknowledgement is needed when Lap is absent. The migration Down deliberately raises an error: replacing the legacy distance with the zero sentinel is not safely reversible.

### Supported deployment procedure for existing installations

The Statistics migration worker runs `Database.MigrateAsync()` for all pending migrations. The main Helm chart creates it as a `pre-install,pre-upgrade` hook Job before application workloads; setting a new image tag alone therefore runs activation before the workloads update. Do not use a normal rolling Helm upgrade as the activation procedure. The maintenance acknowledgement prevents an accidental upgrade, but does not stop writers by itself.

1. Confirm stage one is applied and that the integrated Facilities producer, evidence-persisting Sample writer, Statistics readers/writers, UI and Bootstrap build are ready. Deploy the new application images first while the Lap definition remains Fixed, disabling only the Statistics migration hook for that deployment, for example:

   ```yaml
   module-statistics-api:
     migration:
       enabled: false
   module-statistics-processor:
     migration:
       enabled: false
   migrationJobs:
     domains:
       statistics:
         enabled: false
   ```

   This uses the chart-supported migration switches; keep other domain migrations as required. Verify new samples carry paired layout and closed-loop evidence. Keep Lap name, configured distance and Bootstrap mutations frozen.

2. Plan and execute the message boundary: drain old completions while Fixed and retain/replay messages only as the integrated runtime supports. In-flight attempts with pre-evidence samples must be diagnosed as unresolved or replayed with authoritative evidence, never assigned a fallback after activation. Then stop and drain every Lap entry writer, including Statistics API/processor work and Bootstrap, and take old readers offline. Ensure no old binary can resume after the migration commits. The table locks only block concurrent DML during the transaction; a queued legacy writer could resume after commit.

3. Check definition identity, configured distance, configuration shape, null snapshot count, row counts, database backup/recovery readiness and blocked transactions. Use the Statistics database only; Facilities is not queried by this migration. Verify one exact Lap row, a valid Fixed baseline, at least one unique Simple loop config and no Multi dependencies. Resolve ambiguity or unsupported configuration with operators before continuing; do not rename, relabel or delete rows to force activation.

4. Acknowledge this maintenance window for only the database role used by the Helm Statistics migration Job. In PostgreSQL, ask a DBA to apply a temporary role/database default so the Job connection receives the custom setting:

   ```sql
   ALTER ROLE migration_role IN DATABASE statistics_database SET velotime.lap_distance_activation = $$on$$;
   ```

   Replace the role and database identifiers with the exact values used by the migration Job. The setting is session-local when read by the migration; the role/database default is only a way to pass it to the chart-created Job. Record the original role setting and do not leave this acknowledgement enabled.

5. Run the planned Helm upgrade with the Statistics migration enabled and the maintenance/quiescence in effect. Its pre-upgrade hook runs the migration image, whose `MigrationWorker` calls `Database.MigrateAsync()` for pending migrations. Each EF migration follows its own transaction behavior; this activation migration runs in one transaction. A hook failure blocks the workload rollout. Do not resume old binaries. Confirm the hook completed successfully and query `ef.__ef_migrations_history`, the Lap item and its configuration, and verify all Lap entries have a positive finite `distance_metres` before resuming consumers.

6. Immediately remove the temporary setting, restoring its recorded prior value if one existed, otherwise:

   ```sql
   ALTER ROLE migration_role IN DATABASE statistics_database RESET velotime.lap_distance_activation;
   ```

   Only then resume the already-upgraded readers and writers and observe incoming evidence-backed results. Do not restart old binaries against the activated item. Failed migrations roll back the final sweep and activation; keep services quiesced, clear the acknowledgement, inspect the reported prerequisite/lock failure and retry only after the cause is resolved.

For an empty fresh install, the activation migration sees no Lap and is a no-op without the setting. The updated Bootstrap worker then seeds Lap in CourseLayout mode with the zero sentinel and its loop config; it does not change the Fixed 250m pursuit seed. The Bootstrap worker must run after the schema migrations as usual.

### Data and provider notes

PostgreSQL holds both table locks through validation, backfill, activation and the EF migration-history update. Schedule a maintenance window, allow for lock waits, WAL and replication lag, and do not treat SQLite as proof of PostgreSQL behavior. The final sweep uses the same legacy policy as stage one: exact Lap name, actual configured distance, no profile/validity/facility/moderation filters, update null snapshots only, recompute speed only for positive durations, preserve existing snapshots and nonpositive-duration speeds. PostgreSQL interval epoch conversion includes days and fractional seconds; unsupported calendar-month intervals abort. Keep the original configured baseline and a recovery plan before activation because the zero sentinel and subsequent layout snapshots cannot be mapped back to one fixed distance.
