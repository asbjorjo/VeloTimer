# Profiles persistence

The core owns only the Profiles database. A consuming host registers storage and
the scoped `ILanguagePreferenceService` with `builder.AddModuleProfiles()`.
Configure `ConnectionStrings:profilesdb` (environment variable
`ConnectionStrings__profilesdb`) to a dedicated PostgreSQL database.

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

## Migration and deployment

Following the existing modules, generated migrations and the model snapshot live
in the core project; `Module.Profiles.Migration` is the executable migration
worker using the shared `MigrationWorker<ProfilesDbContext>`.

The initial migration `20261030153543_profiles_initial` creates
`profiles.language_preference`, with non-null issuer, subject, and language,
a composite primary key, PostgreSQL `C` collation for exact comparisons, and
a check constraint accepting only the two canonical languages. EF migration
history uses `ef.__ef_migrations_history`, matching shared storage registration.
There is no seed, backfill, cross-service access, audit history, or outbox event.
The current common base context does not generate row-history tables.

Provision the dedicated database and provide a migration identity with schema
and table creation privileges, then run the migration worker before serving
Profiles requests. Runtime access needs schema usage plus select/insert/update
on the preference table; runtime hosts do not automatically migrate. No existing
service database or migration is changed. Rolling back this initial migration
drops saved preferences; do not roll it back on a populated database without
an explicit data-loss plan. PostgreSQL's normal B-tree index-entry size limits
apply to very large issuer/subject pairs; validate integration with the actual
identity provider rather than assuming arbitrarily long identities will fit.

For design-time tooling, the context factory reads
`ConnectionStrings__profilesdb`; absent that setting, it uses a password-free
localhost `profilesdb` connection for model/script generation.

### Aspire and Helm

The AppHost provisions `profilesdb` separately from `velotimedb`, passes the
validated application's `OIDC_AUDIENCE` and Keycloak reference to the Profiles
API, and waits for the Profiles migration worker before starting that API.
Only WebUI receives the Profiles API discovery reference; no other domain reads
the Profiles database. Profiles does not require Redis or a messaging processor.

For Kubernetes, provision a dedicated PostgreSQL database and a Secret named
`velotime-profiles` containing `ConnectionStrings__profilesdb`. Never point this
connection at another module's database. The Secret must exist before Helm hooks
run; it is not generated from chart values. Override its name with
`module-profiles-api.existingSecret` in the application chart and
`velotime-module-profiles-migration.existingSecret` in the migration chart.
Configure `global.oidcAudience` and `global.oidcAuthority` in the application
chart, as for the other APIs. No additional Keycloak client or user migration is
required because the API validates the existing user's audience-bound token.

The application chart's Profiles deployment always runs the migration worker as
an init container before serving traffic. The migration chart additionally
provides a pre-install/pre-upgrade Profiles schema job for installations that
apply migrations separately. Both use the same idempotent EF migrations.
The deployment uses `/health` for database/schema readiness and `/alive` for
liveness. The Profiles API remains internal to the cluster; WebUI calls it using
service discovery and delegates the authenticated user's token.

The existing solution-wide publish workflow discovers the API and migration
worker without a workflow-specific project list. They publish
`asbjorjo/velotime-module-profiles-api` and
`asbjorjo/velotime-module-profiles-migration` with the shared application version.
Migration init containers currently use the same Secret as the API, following
the other module chart conventions, so that identity needs migration privileges
as well as runtime access.

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
