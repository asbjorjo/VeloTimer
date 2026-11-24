# Statistics sample identity activation

`20261123131028_statistics_sample_timing_identity` was an expand-only migration.
`20261123151518_statistics_sample_identity_activation` is the destructive activation:
it deletes **all** `statistics.statistics_entry` and `statistics.sample` rows,
including already-keyed samples, Simple and Multi entries, manual invalidation audit,
validity and distance snapshots. Legacy samples lack Timing point IDs and old random
entry IDs lack contributor lineage; neither can be reconstructed safely by matching
times, course points, or current Facilities associations. The reset is deliberately
whole-result, not a purported selective backfill. Definitions, configs, profiles,
Timing, Facilities, and `statistics.smb_outbox` are not deleted. Every existing
revalidation checkpoint is restarted at Simple, cursor cleared, generation advanced,
and missing item checkpoints are inserted. Overflow or any SQL error rolls everything
in this migration back.

The final Timing point columns are NOT NULL and the unique index covers the normalized
`(transponder_id, timing_point_start_id, time_start, timing_point_end_id, time_end)`.
There is no cross-service FK. New writers normalize UTC instants to PostgreSQL timestamp
precision (`StatisticsStoragePrecision.Timestamp`) before lookup and insertion.
The old pair check and partial index are removed. A missing source ID now fails at the
database, but the database cannot recognize an old random-entry-ID writer or a stale
message: **the maintenance and message barrier below is mandatory even on an empty
installation**. Never run old processors after activation.

## Maintenance procedure

1. Inventory, export and test a consistent restorable backup, including definitions,
   profiles, all derived data, checkpoints and both isolated outboxes. Inventory
   consumers, downstream projections, caches, broker topics/subscriptions, scheduled
   messages, in-flight locks, retries and dead letters. Stop API definition changes,
   all old and new Statistics processors, revalidators and dispatchers, and any other
   writers/consumers relevant to these messages. Wait for in-flight transactions and
   broker deliveries to finish or explicitly quarantine them; revoke/disable old
   deployments so they cannot resume after locks are released.
2. Coordinate TimingSampleComplete, SampleComplete and EntryCreated queues and
   dead letters with their owners. A delivered message or outbox mark proves neither
   downstream completion nor that redelivery is impossible. Quarantine or individually
   reconcile all old pending, delivered, delayed, in-flight and dead-letter messages
   and their downstream effects. A stale message delivered after reset can recreate
   partial results with different identities; a database lock cannot prevent it.
   Preserve and handle Statistics threshold wakeups and the API's independent
   `statistics.smb_outbox` under their own protocol; never clear that shared outbox.
3. Both `statistics.sample_complete_outbox` and `statistics.entry_created_outbox`
   must be **empty**; the migration fails if either has even one delivered receipt,
   including a SampleComplete with a missing entries-created mark. It never deletes
   these records or infers delivery. An empty table is not proof of broker or consumer
   state. If retained receipts exist, stop: archive and reconcile each one against
   the broker, consumers and backup with the messaging owner. Only a separate,
   explicitly approved receipt-removal procedure after that reconciliation can make
   the tables empty; this migration supplies no automatic purge or delivery inference.
4. Verify the deployed schema and external dependencies. The migration rejects inbound
   FKs, user triggers, rules and inheritance involving the reset tables; this is not
   an inventory of external databases, logical replication or consumers. It locks
   items, checkpoints, samples, entries and isolated outboxes until commit. Locks
   protect the transaction, not writers queued behind it. Plan for exclusive locks,
   DELETE/WAL, table scans, index build, disk, vacuum and table-owner DDL privileges.
5. Apply the migration with only the migration connection configured with Npgsql
   `Options=-c velotime.statistics_sample_reset=approved` **after** completing the
   barrier. This explicit opt-in is required even on an empty database, because stale
   broker messages and mixed-version writers are not visible to the migration. It is
   not a substitute for the barrier.
   Do not set it globally or leave it on pooled application connections. The migration
   worker normally auto-applies pending migrations; disable it until the procedure is
   ready. Verify migration history (`ef.__ef_migrations_history`), empty result and
   isolated outbox tables, NOT NULL columns, unique index and restarted checkpoints.
   Earlier migrations have their own transaction boundaries (some use concurrent
   indexes); failure of activation does not undo earlier successful migrations.
   The preceding Lap activation has independent validation and may block the upgrade.
   For local Aspire, the one-invocation `STATISTICS_SAMPLE_RESET_APPROVAL=approved`
   option and its fresh-install prerequisites are described in the
   [root README](../../../README.md#statistics-sample-activation-on-local-startup).
   For Helm, provision a separate migration-only Secret with the connection string
   including that Npgsql `Options` value, and select it using
   `migrationJobs.domains.statistics.existingSecret`. The Secret must exist before
   the Helm hook runs. Remove the override and approval-bearing Secret after
   successful activation. Do not add the option to the API's default Secret or
   chart-managed runtime database values. Helm's pre-upgrade hook does not stop
   existing workloads, enforce the maintenance barrier, or inspect broker state;
   complete the barrier before invoking Helm.
6. Deploy only replay-safe processors and compatible consumers. Refresh caches,
   restart the revalidator, and monitor errors, queues, dead letters, pending outboxes
   and checkpoint completion. Explicitly reconcile downstream projections; a reset
   cannot retract already published events. Any redrive must be separately reviewed
   and ordered per transponder.

Deletion is irreversible without a tested consistent backup; Down explicitly fails.
Restoring only Statistics after new events have escaped is not a complete rollback.
Stop and coordinate broker/downstream restoration or prefer roll-forward.
The reset does **not** regenerate historical results. There is no established complete,
ordered historical replay tool in this repository. Even if one is supplied, missing
or expired events, changed Facilities layouts/associations, definitions, thresholds,
ordering and manual audit mean regenerated history may differ or be incomplete.
Do not promise historical fidelity; stop if the approved loss is unacceptable.

PostgreSQL integration tests create/drop fixture-owned databases; they do not prove
the production broker barrier or external consumers. No shared database was migrated.
