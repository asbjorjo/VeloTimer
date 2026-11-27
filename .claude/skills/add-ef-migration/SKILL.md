---
name: add-ef-migration
description: Add, check, or remove an EF Core migration for a VeloTimer module (Timing, Facilities, Statistics, Profiles, Notifications, or Agent). Use after changing an entity, DbContext configuration, index, or constraint, or to check whether a model change needs a migration.
---

# Add an EF Core migration

Migrations live in the **core** project, `src/Module.{Domain}/Migrations/` (or `src/Agent/Migrations/`). The `src/Module.{Domain}.Migration/` projects only *run* them, so never add migrations there. Each DbContext has an `IDesignTimeDbContextFactory`, so EF tooling needs no running database or startup project.

| Module | Project | DbContext |
| --- | --- | --- |
| Timing | `src/Module.Timing` | `TimingDbContext` |
| Facilities | `src/Module.Facilities` | `FacilityDbContext` |
| Statistics | `src/Module.Statistics` | `StatisticsDbContext` |
| Profiles | `src/Module.Profiles` | `ProfilesDbContext` |
| Notifications | `src/Module.Notifications` | `NotificationsDbContext` |
| Agent outbox | `src/Agent` | `AgentDbContext` |

## Steps

1. Restore the pinned tool once per checkout: `dotnet tool restore`.
2. Check whether the model actually changed:

   ```bash
   dotnet ef migrations has-pending-model-changes --project src/Module.Timing
   ```

3. Add the migration with a descriptive PascalCase name (recent migrations look like `AddTransponderOwnerSurrogateKey`):

   ```bash
   dotnet ef migrations add AddSomething --project src/Module.Timing
   ```

4. Review the generated `<timestamp>_AddSomething.cs`, its `.Designer.cs`, and the `*ModelSnapshot.cs` diff:
   - The tables belong to the module's own default schema (`timing`, `facility`, `statistics`, `profiles`, `notifications`, `agent`). Nothing should touch another module's schema.
   - Columns are snake_case, timestamps are UTC (`timestamp with time zone`), and quantities are in base SI units.
   - Check data safety: new non-null columns need a default or a backfill, renames should not show up as drop-and-add, and indexes on large tables need a deployment note.
   - Hand-written SQL (`migrationBuilder.Sql`) must be idempotent where re-runs are possible.
5. Run `has-pending-model-changes` again. It must report no changes.
6. Build the core project and its `*.Migration` runner: `dotnet build src/Module.Timing.Migration`.
7. Commit the migration, designer, snapshot, and model change together, for example `feat(timing): add transponder nickname column`.

## Removing a migration

Only a migration that has **not** been committed to a shared branch or applied anywhere can be removed: `dotnet ef migrations remove --project src/Module.Timing`. Never edit or delete a migration that has already been applied.

## Do not

- Run `dotnet ef database update`, `database drop`, or apply migrations to any shared or remote database without explicit approval. Migrations are applied at startup by `MigrationWorker` in each runner and by the Helm migration hooks.
- Assume a successful `migrations add` proves the migration applies. PostgreSQL-backed tests (see the `run-tests-locally` skill) or a local AppHost run do that.
