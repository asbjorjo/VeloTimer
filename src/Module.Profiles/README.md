# Profiles persistence

The Profiles core owns its data in the `profiles` schema of the existing
`velotimedb` PostgreSQL database. A consuming host registers storage and the scoped
`ILanguagePreferenceService` with `builder.AddModuleProfiles()`. Configure
`ConnectionStrings:velotimedb` (environment variable
`ConnectionStrings__velotimedb`) to the shared application database.

## Service contract

Namespace: `VeloTime.Module.Profiles.Service`

```csharp
Task<LanguagePreferenceDTO> GetAsync(
    string issuer, string subject, CancellationToken cancellationToken = default);

Task<LanguagePreferenceDTO> UpdateAsync(
    string issuer, string subject, string? language,
    CancellationToken cancellationToken = default);
```

The API must obtain issuer and subject from an already validated authentication
token, never from request data or a caller-selected user ID. Core validation
rejects blank identity components but does not authenticate tokens or determine
trusted issuers. Identity comparisons are exact and case-sensitive, with no
trimming, URL normalization, or case folding.

Reads do not create rows and return `LanguagePreferenceDTO(null)` for a missing
preference. Updates accept exactly `en` or `nb-NO`, as defined by the shared
contract's `ProfileLanguageCodes.IsSupportedPreference`. Null is not a reset
operation. Blank identity components or unsupported languages cause an
`ArgumentException` before database access; its `ParamName` identifies the
invalid input (`issuer`, `subject`, or `language`).

Updates use one parameterized PostgreSQL `INSERT ... ON CONFLICT DO UPDATE`.
The composite issuer/subject primary key arbitrates concurrent first writes;
no read-before-insert or unique-violation recovery is needed. Concurrent updates
are last-write-wins in database execution order, not request arrival order.
The response contains the value written by that operation, without a second
read. Normal PostgreSQL transaction isolation rules still apply if callers
introduce explicit transactions (e.g. serializable transactions can require
whole-transaction retries). Writes use SQL directly rather than tracked
entities; service reads are always no-tracking.

## Display preference service

Namespace: `VeloTime.Module.Profiles.Service`

```csharp
Task<DisplayPreferenceDTO> GetAsync(
    string issuer, string subject, CancellationToken cancellationToken = default);

Task<DisplayPreferenceDTO> SetAsync(
    string issuer, string subject, UpdateDisplayPreferenceRequest request,
    CancellationToken cancellationToken = default);
```

`DisplayPreferenceService` is registered as a scoped concrete service by
`AddModuleProfiles()`. Identity inputs must come from the validated token. Reads
are no-tracking and return `m` / `km/h` / `browser` / null when no row exists;
this default is not persisted. Writes replace the complete snapshot and return
that submitted value without rereading. They do not update language preferences.

Distance codes are exactly `m` or `ft`; speed codes are exactly `km/h`, `mph`,
or `m/s`; time modes are exactly `browser`, `utc`, or `timeZone`. Browser and
UTC modes require a null time-zone ID. Named mode requires a nonblank ID exactly
matching the IANA zone catalogue derived from
`TimeZoneInfo.GetSystemTimeZones()` on the running .NET host.
This prevents Windows aliases, case folding, whitespace normalization, and UTC
fallback from silently changing the requested zone. Validation completes before
opening the database connection. Invalid input throws
`DisplayPreferenceValidationException`, an `ArgumentException` with a stable
`ErrorCode` from `ProfilePreferenceErrorCodes` and an identifying `ParamName`.
The API maps those codes to its contract problem response.

Persistence uses a parameterized PostgreSQL `INSERT ... ON CONFLICT DO UPDATE`
against the separate display table. The composite issuer/subject primary key
arbitrates concurrent first writes. SQL write responses do not reread, so
concurrent writes are last-write-wins in database execution order. The new table
has no data backfill and does not change the existing language table.

## Migration and deployment

Following the existing modules, generated migrations and the model snapshot live
in the core project; `Module.Profiles.Migration` is the executable migration
worker using the shared `MigrationWorker<ProfilesDbContext>`.

The initial migration `20261030153543_profiles_initial` creates
`profiles.language_preference`, with non-null issuer, subject, and language,
a composite primary key, PostgreSQL `C` collation for exact comparisons, and
a check constraint accepting only the two canonical languages. The following
`20261031115140_display_preference` migration creates
`profiles.display_preference` with exact-collation issuer/subject keys,
supported-code checks, and a time-mode / time-zone nullability check. It performs
no backfill and leaves the language table untouched. EF migration history uses `ef.__ef_migrations_history`, matching shared storage
registration. There is no seed, backfill, cross-service access, audit history, or
outbox event.
The current common base context does not generate row-history tables.

Provide the existing `velotimedb` connection with schema and table creation
privileges to the migration worker, then run that worker before serving Profiles
requests. Runtime access needs schema usage plus select/insert/update on both
preference tables; runtime hosts do not automatically migrate. The migrations
create only the `profiles` schema and Profiles-owned tables in `velotimedb`; they
do not alter another module's schema or tables. Rolling back the
display-preference migration drops saved display preferences; rolling back the
initial migration also drops language preferences. Do not roll back either
migration on a populated database without an explicit data-loss plan.
PostgreSQL's normal B-tree index-entry size limits apply to very large issuer/subject pairs; validate integration with the actual
identity provider rather than assuming arbitrarily long identities will fit.

For design-time tooling, the context factory reads
`ConnectionStrings__velotimedb`; absent that setting, it uses a password-free
localhost `velotimedb` connection for model/script generation.

### Aspire and Helm

The AppHost references the existing `velotimedb` for the Profiles API and
migration worker, passes the validated application's `OIDC_AUDIENCE` and
Keycloak reference to the Profiles API, and waits for the Profiles migration
worker before starting that API.
Only WebUI receives the Profiles API discovery reference; no other domain reads
the Profiles database. Profiles does not require Redis or a messaging processor.

For Kubernetes, configure the Profiles API chart's `database` values for the
existing PostgreSQL service, or set `module-profiles-api.existingSecret` to a
Secret containing `ConnectionStrings__velotimedb`. The chart's default Secret
contains the same `velotimedb` connection fields used by the other service
charts. Profiles only writes to its own schema.
Configure `global.oidcAudience` and `global.oidcAuthority` in the application
chart, as for the other APIs. No additional Keycloak client or user migration is
required because the API validates the existing user's audience-bound token.

The application chart runs the Profiles schema migration worker as a
`pre-install,pre-upgrade` Job in the same Helm release as the API. Helm waits for
the Job to complete before creating or updating the ordinary application
resources; Profiles pods no longer run migrations on restart or scale-out.
The separate `velotime-migration` chart remains reserved for migrating data from
legacy installations and does not run Profiles schema migrations.
Use `module-profiles-api.migration.enabled` or the explicit
`migrationJobs.domains.profiles.enabled` override to control execution.
The deployment uses `/health` for database/schema readiness and `/alive` for
liveness. The Profiles API remains internal to the cluster; WebUI calls it using
service discovery and delegates the authenticated user's token.

The existing solution-wide publish workflow discovers the API and migration
worker without a workflow-specific project list. They publish
`asbjorjo/velotime-module-profiles-api` and
`asbjorjo/velotime-module-profiles-migration` with the shared application version.
Migration configuration defaults to the API's database values or `existingSecret`.
Chart-managed credentials are rendered into a separate migration-only hook Secret;
the API's Secret remains an ordinary Helm-managed resource. Set
`migrationJobs.domains.profiles.existingSecret` to a pre-provisioned migration
Secret to separate schema-changing privileges from runtime privileges.
Both Secrets use `ConnectionStrings__velotimedb`. See the root
[Helm schema migration guide](../../README.md#helm-schema-migrations) for
configuration precedence, hook configuration retention/cleanup, and rollback
limitations. Existing API pods can remain active during migration, so schema
changes must remain compatible with the running version.

## PostgreSQL verification handoff

The Tester should verify migration application and reapplication on an isolated
PostgreSQL database, missing reads leaving the table empty, identity isolation
(including equal subjects from distinct issuers and case differences), rejection
of null/noncanonical languages, direct database constraint enforcement, and
concurrent first writes from separate contexts resulting in one row with a
supported value and no unique-violation failures. Also cover sequential updates,
reads after SQL upserts in the same scope, cancellation, and realistic identity
provider issuer/subject lengths. SQLite does not validate the PostgreSQL
collation, upsert, migration history, or concurrency behavior.
