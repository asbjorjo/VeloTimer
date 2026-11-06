# Notifications API/core-service phase handoff

## Status: storage prerequisite blocks final feature commit

The full API, transactional service, mapper, snapshot service and generated client
are implemented in the assigned worktree, not stub handlers. **Do not declare the
feature complete or deploy it yet.**

Data & Migrations revision required (reported before any schema edits):
`NotificationsDbContext` and the unapplied initial migration contain
`ck_notification_schedule: ends_at_utc IS NULL OR ends_at_utc > starts_at_utc`.
The approved contract explicitly allows cancelling a future notice by ending it at
server-now **without changing its start**. `NotificationService.EndAsync` implements
that contract, so a future cancellation currently violates this storage check.
Data owner must revise the invariant/migration to represent a cancelled schedule
with end <= start. Create/update still enforce end > start in the service. No model,
context or migration files have been modified by the API owner.

This schema blocker is why the dependent feature implementation remains uncommitted.
Verified, independent shared-auth/enum/policy/mapper changes can be committed separately.
After Data owner repairs the prerequisite, API owner must revalidate and commit the
dependent feature before Tester/Reviewer handoff.

## Actual artifacts and downstream protocol

* `Module.Notifications.Interface/Identity/NotificationSessionAssertion.cs` and its
  README: **actual shared RSA-PSS/SHA256 login assertion**, ticket-property/header
  constants, exact signer/configuration/rotation/expiry contract. No embedded keys.
  UI/BFF must create a fresh random ID at actual successful application login, preserve
  it in the protected ticket on renewals, sign per forwarded validated access token,
  and never expose the ticket ID or assertion to browsers/logs/SignalR.
* `Module.Notifications/Service/NotificationService.cs`: manual authoring
  `CreateAsync(CreateNotificationRequest)`, admin `GetAsync/UpdateAsync/EndAsync`,
  authenticated `DismissAsync/RestoreAsync`. Caller authorization is API-owned;
  background producers must not bypass targeting/policy validation. No initial event
  producers/recipes are approved.
* `NotificationSnapshotService`: active/admin/dismissal lists, repeatable-read
  PostgreSQL snapshots, UUID keysets, encrypted identity+role+revision-bound
  continuations, maximum 300-second lifetime, entire-authorized-set deadlines.
  Page size 1..100; null token begins a snapshot. Invalid/mismatched token => 400;
  role/revision/expiry change => 409. Each page has fresh server-now and stable as-of,
  expiry, active next-change. Drain **all** pages before replacing a UI snapshot.
* `Module.Notifications.Api/Endpoints/NotificationEndpoints.cs`: all nine routes
  from `NotificationRoutes`, static typed handlers, no-store responses, no public
  recipient/admin projections. Only genuine-anonymous active is public.
* `api/VeloTime.Module.Notifications.Api.json`: generated OpenAPI, exact enum strings,
  Bearer security, required restore query, BFF-only assertion header, typed Problems
  with required `code`.
* `Module.Notifications.Client`: NSwag `INotificationsClient`/`NotificationsClient`
  generated at build, shared Interface DTOs/enums (not divergent generated copies),
  System.Text.Json camelCase, DateTimeOffset UTC, `AddNotificationsClient()` service
  discovery target `https+http://module-notifications-api`. `RestoreAsync` extension
  maps the shared typed restore enum to exact query strings. Non-success throws
  `ApiException<ProblemDetails>` with `Result.Code`; undocumented failures retain
  raw response/status. No empty-snapshot or saved-state fallback. BFF forwards Bearer
  using its request-scoped token integration; generated methods accept the signed
  `veloTime_Notification_Session` header argument (null only for genuine anonymous).

## Worker implementation contract

No workers were implemented here. Service writes tracked authoritative state,
schedule slots, global revision (context SaveChanges), and one minimal refresh
outbox row in one database transaction. Mutation lock order:

1. Notice `SELECT ... FOR UPDATE` (also fences absent dismissal keys on that UUID).
2. Existing account/current-session source rows `FOR UPDATE`, when relevant.
3. Corresponding scheduled slots `FOR UPDATE`.
4. Tracked save/revision increment, commit.

This deliberately serializes different readers' writes to the same notice to avoid
absent-key races; it does not overwrite other login rows. Different-account choices
replace the account row; identical effective retries preserve original times/expiry
and emit no hint. Session writes are separate and do not clear account suppression.
Restore cancels selected source slots and removes only account/current-login layers.
Edits retain dismissal rows/UUID suppression.

Start/end slot source ID = notification UUID; duration slot source ID = account row
ID. Rescheduling increments generation and clears claim/completion/attempt state.
Removing a deadline removes its slot. Workers must fence leases/generations and
lock the authoritative source before its slot as documented in the storage README.
End cancels a future activation rather than scheduling it. No session-expiry jobs
are fabricated: there is no trusted ticket expiry/revocation feed into storage.
Old login rows cease to apply when that login ID is no longer asserted; garbage
collection needs a separate agreed trusted lifecycle/retention policy.

Outbox rows use `EventId` as `NotificationsRefreshRequestedV1` and transport ID;
publish the existing static broadcast contract only, with runtime provisioning off.
Source-domain workers and browser hub semantics are unchanged/out of scope.

## Platform and UI prerequisites

Wire API deployment/discovery, `velotimedb` connection and service-owned migration,
existing `OIDC_AUDIENCE`/Keycloak trust and assertion public-key configuration.
Notifications admin requires only the existing transformed
`velotime_permission=velotime.admin`, never a new realm role.
API assertion options fail startup when issuer/audience/trusted RSA public keys
are missing or invalid. OpenAPI build is exempt from startup secret configuration.

Share a **durable, protected Data Protection key ring** across API replicas, application
name `VeloTime.Notifications`. Configure at-rest protection with infrastructure-owned
secrets/key storage, not repository keys. Default local dev key storage emits runtime
warnings and is not production configuration. JWT and signed assertion both require
TLS; configure BFF cookie/CSRF/logout separately. Anonymous dismissal remains UI-only
per-tab sessionStorage, with no backend state or implicit login migration.

## Actual validation

* Interface Release builds: success, zero warnings/errors.
* Notifications API + generated-client Release builds: success, zero warnings/errors.
* `OIDC_AUDIENCE=https://build.invalid dotnet build VeloTimer.sln --configuration Release`:
  success, **128 repository warnings**, zero errors (includes existing package security,
  SDK and nullable warnings outside this feature). No packages/central versions changed.
* `OIDC_AUDIENCE=https://build.invalid dotnet test
  src/Module.Profiles.Api.Test/VeloTime.Module.Profiles.Api.Test.csproj -c Release
  --no-build --no-restore`: 160 passed, 28 skipped, zero failed. PostgreSQL tests skipped.
* Temporary implementation probe (not repository tests, Tester owns adding tests):
  `/tmp/notifications-api-probe/Probe.csproj`, run with
  `OIDC_AUDIENCE=https://build.invalid dotnet run --project
  /tmp/notifications-api-probe/Probe.csproj -c Release`: **75 passed non-database checks**.
  Includes real API TestHost + validated locally signed Bearer token, assertion
  signature/issuer/subject/access-token/audience/expiry/ticket cap, exact enum rejection,
  policy bounds/defaults, UTC start/end/snooze boundaries, no-store auth/admin errors,
  malformed/expired/identity/role-mismatched encrypted snapshots, generated-client
  scope serialization/shared DTO parsing and all documented error statuses/code.
  PostgreSQL UUID/audience SQL was translated but **not executed**.
* TCP check `127.0.0.1:25432`: connection refused (111). Real anonymous active was
  verified to return **503**, not successful empty content. No migration was applied,
  no PostgreSQL mutation/transaction/concurrency/deadline-worker tests ran.

## Tester acceptance scenarios after prerequisite repair

1. Create severity defaults/complete overrides; reject missing/null fields, numeric
   and case-aliased enum codes, unknown fields, invalid audience/UTC/schedule/duration.
   Reader projections never include target/issuer/subject/login/author metadata.
2. Everyone/anonymous/authenticated/exact role/exact issuer+subject matrix, including
   issuer/case differences. Invalid token is 401, never anonymous fallback.
   Authenticated missing/tampered/expired/wrong-subject/token assertion is explicit 403.
3. Each default/allowed/forbidden scope; account and two concurrent logins remain
   independent. Concurrent first writes produce one row per key; identical retries
   preserve expiry/outbox count, expiry retries restart duration. All restore scopes,
   repeated no-op restore/end, old version 409, future cancellation with unchanged start.
4. Start inclusive/end exclusive/snooze expiry exclusive, future/ended management,
   policy tightening preserves existing valid dismissals, edit never silently revives
   dismissed UUIDs. PostgreSQL microsecond timestamp precision should be covered.
5. >100 notices, random UUID insertion behind cursors, policy/audience/dismissal writes
   between pages => revision-expired 409, no omissions/duplicates; deadlines account
   for future/suppressed/outside-page authorized notices and exclude private targets
   from anonymous snapshots. Restore at response-time may still be inactive.
6. Mutation rollback/outage leaves neither saved state nor orphaned schedule/outbox;
   source/slot locking races with worker claims, stale generations and deleted sources.
   Durable start/end/duration invalidations and reconnect recoverability after restart.
7. BFF fresh login vs SSO sid reuse, refresh/reconnect stability, logout clears private
   content immediately, signing rotation and <=120-second default revocation bound;
   cross-replica Data Protection tokens and no credentials in browser/logs.
8. Client transport/cancellation/malformed JSON/non-success remain explicit failures;
   profile/API existing-service regression. Worker/UI/platform end-to-end validation
   remains their respective ownership, with Tester adding persisted automated checks.

Next owner: **Coordinator → Data & Migrations** for schedule invariant repair, then
API owner final validation/feature commit, then Tester and read-only Reviewer.
