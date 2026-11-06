# Notifications API/core-service phase handoff

## Status: API phase complete; cancellation prerequisite resolved

Data owner commit `1455e17333a40f881ea502f461b84aa96b5bca11` supplied corrective
migration `20261106094745_notifications_allow_cancellation`, removing the old
ordering constraint. Both migrations were applied to an isolated PostgreSQL 17
database in this phase. Future End now persists an empty cancelled interval with
end <= start and **preserves the original start**. Create/update still require
end > start. No model, context or migration files were modified by the API owner.
There is no unresolved Notifications data prerequisite.

This completes the API phase, not integrated deployment/push acceptance. UI,
platform, worker implementations and Tester/Reviewer gates remain separate.

## Actual artifacts and contracts

* [Shared Interface contract](../Module.Notifications.Interface/README.md):
  exact nine routes, DTOs, enum codes, audience/policy/defaults, paging and errors.
  No request identity, author/source metadata or display-unit conversion.
* `Service/NotificationService.cs` in the core module: manual authoring
  `CreateAsync(CreateNotificationRequest)`, admin `GetAsync/UpdateAsync/EndAsync`,
  authenticated `DismissAsync/RestoreAsync`. API owns caller authorization;
  background producers must not bypass targeting/policy validation. No initial
  source-event producers/recipes are approved.
* `Service/NotificationSnapshotService.cs`: active/admin/dismissal lists,
  repeatable-read PostgreSQL snapshots, UUID keysets, encrypted identity+role+
  revision-bound continuations. Maximum lifetime 300 seconds; page size 1..100,
  default 50. Null token begins a snapshot. Invalid/mismatched token => 400;
  role/revision/expiry change => 409. Fresh server-now per page, stable as-of,
  expiry and active next-change. Deadlines cover the entire authorized set,
  including future/suppressed/outside-page notices, excluding private targets
  from anonymous snapshots. Drain **all** pages before replacing a UI snapshot.
* `Endpoints/NotificationEndpoints.cs`: all nine `NotificationRoutes`, static
  typed handlers, no-store responses, no reader recipient/admin projection.
  Only genuine-anonymous active is public. Invalid bearer credentials never
  downgrade to anonymous success.
* `api/VeloTime.Module.Notifications.Api.json`: generated OpenAPI, exact string
  enums, required restore query `scope`, BFF-only assertion header, typed Problems
  with required `code`. DELETE has no body; only
  `account|currentSession|accountAndCurrentSession` are valid query values.
* `Module.Notifications.Client`: build-generated NSwag
  `INotificationsClient`/`NotificationsClient`, shared Interface DTOs/enums rather
  than divergent copies, System.Text.Json camelCase, DateTimeOffset UTC.
  `AddNotificationsClient()` targets `https+http://module-notifications-api`.
  Typed `RestoreAsync` extension maps the shared restore enum to exact query codes.
  Non-success throws `ApiException<ProblemDetails>` with `Result.Code`;
  undocumented failures retain raw response/status. No success-shaped fallback.
  BFF forwards its validated bearer token via request-scoped token integration;
  generated methods take the signed `veloTime_Notification_Session` argument
  (null only for genuine anonymous).
* `VeloTimer.sln`: API/client projects registered alongside Interface/core/migrator.

### Timestamp precision decision

Persisted state and mutation response timestamps use PostgreSQL microseconds.
`Service/NotificationTimestamp.cs` matches Npgsql's truncation relative to its
2000-01-01 UTC epoch, including historical dates. Authoring start/end and mutation
server-clock values are canonicalized before persistence and DTO mapping.
An authoring interval collapsing to end <= start at that precision is rejected
with 400 `notifications.invalid_schedule`, never accidental cancellation.
Fresh response/snapshot server-now remains UTC clock time. DTO shapes are unchanged;
UI must use returned authoritative timestamps, not echo request values.

## Exact application-login protocol / UI prerequisites

[Shared signer/verifier and configuration](../Module.Notifications.Interface/Identity/README.md)
are actual artifacts, not a proposal for an unsigned browser header:

* Header: `VeloTime-Notification-Session`.
* Protocol: `velotime.notifications.login.v1` (RSA-PSS/SHA256, >=2048-bit RSA).
* Protected AuthenticationProperties key: `.notifications.application_login`.
* On successful fresh application OIDC login, BFF creates
  `NotificationSessionAssertion.NewApplicationLoginId()` and stores it in that
  protected ticket. Preserve across cookie renewal, token refresh and reconnect;
  new login replaces it. Do not use Keycloak `sid` or browser-selected IDs.
* Sign each forwarded validated access token using exact issuer/subject, ticket
  login ID and actual ticket expiry. Strip incoming browser assertion headers.
  Never expose assertions, keys or login IDs to browser JS, URLs, DTOs, SignalR,
  telemetry or logs. TLS required.
* Missing/invalid authenticated assertion => 403
  `notifications.login_session_required`; JWT failure => 401.
  Default assertion lifetime 120 seconds, capped by ticket expiry; API max 300,
  skew default 5 (0..10), no expiry grace. Logout revocation is bounded by issued
  assertion lifetime; UI immediately clears private content/stops forwarding.
* Anonymous dismissal remains UI-only per-tab sessionStorage with no backend
  state or implicit migration into account state on login.

## Platform prerequisites / exact configuration

Wire API discovery/deployment, `velotimedb` connection and service-owned migrator.
Apply **both** Notifications migrations before enabling End. Existing
`OIDC_AUDIENCE`/Keycloak trust applies; admin policy is `Notifications.Admin`,
requiring transformed `velotime_permission=velotime.admin`, not a new realm role.

Environment nesting uses `__`. API options under `Notifications:SessionAssertion`:
`Issuer`, `Audience` (separate from OIDC audience), `PublicKeys:<kid>` (RSA public
PEM), `MaximumLifetimeSeconds` (default 120, 1..300), `ClockSkewSeconds` (default 5,
0..10). Missing/invalid trust fails startup; OpenAPI discovery is exempt.
BFF implements matching Issuer/Audience plus `KeyId`, secret-store-injected
`PrivateKeyPem`, `LifetimeSeconds` (default 120, <= API maximum). Install new public
kid on all API replicas before changing signing kid; retain old public key for
maximum lifetime + skew, then remove. Emergency removal revokes that signing key.

Share a durable, protected Data Protection key ring across API replicas with
application name `VeloTime.Notifications`, purpose `Notifications.Snapshot.v1`.
At-rest protection is infrastructure-owned; no keys belong in source control.
Default local key storage warnings are not production configuration. BFF owns
cookie/CSRF/logout. API image repository is
`asbjorjo/velotime-module-notifications-api`, container port 8081,
version `2.1.0-alpha.33`. Anonymous `/health` requires storage/revision schema;
`/alive` is storage-independent.

## Worker implementation contract

No workers were implemented here. State, schedule slots, global revision
(context SaveChanges) and one minimal refresh outbox row commit in one transaction.
Lock order: notice `FOR UPDATE` (fences absent dismissal keys), existing account /
current-login source rows, corresponding slots, tracked save/revision, commit.
This serializes writes on a notice; it does not overwrite other login rows.
Account duration/permanent replace only account state; session writes retain it.
Identical effective retries preserve original times/expiry and emit no hint.
Restore cancels selected slots/removes only account/current-login layers;
edits/policy tightening retain dismissal rows/UUID suppression.

Start/end slot source ID = notification UUID; duration source ID = account row ID.
Rescheduling increments generation and clears claims/completion/attempts.
Removing a deadline removes its slot. End cancels future activation, never moves
start. Workers must lock authoritative source before slot, fence lease/generation/
exact due time and treat end <= start as never active. See storage README for claims.
No session-expiry jobs are fabricated without a trusted ticket lifecycle feed.
Old login rows cease to apply when no longer asserted; garbage collection needs an
agreed trusted retention/revocation policy.

Outbox `EventId` is both `NotificationsRefreshRequestedV1` ID and transport MessageId.
Use existing static broadcast only: topic `velotime-notifications`, subscription
`webui-notifications`, header `MessageName`, filter
`MessageName = 'NotificationsRefreshRequestedV1'`, non-session; hub method
`NotificationsRefresh`, same minimal event. No content, notification ID, revision,
identity, target or private broker headers. Runtime provisioning off. Platform
owns topology and SignalR Redis backplane for the competing bridge subscription.
Source-domain consumers/browser hub behavior were not modified in this phase.

## Exact validation (2026-11-06)

Commands ran in `/workspaces/VeloTimer-notifications`, branch `feat/notifications`.
The original API phase used temporary implementation probes; those were replaced
for core/API acceptance by the durable `Module.Notifications.Api.Test` project
described below. The probe counts remain historical evidence only.

* `OIDC_AUDIENCE=https://build.invalid dotnet build
  src/Module.Notifications.Client/VeloTime.Module.Notifications.Client.csproj
  -c Release --no-restore`: **0 warnings / 0 errors**, including Interface/core/API,
  regenerated OpenAPI and NSwag client. Discovery logs local Data Protection
  persistence/at-rest warnings, requiring production platform configuration.
* `OIDC_AUDIENCE=https://build.invalid dotnet build VeloTimer.sln -c Release
  --no-restore`: final success, **28 repository warnings / 0 errors** (incremental).
  Earlier solution rebuild: **101 warnings / 0 errors**. These include existing
  package vulnerability/SDK/compiler warnings outside Notifications.
  No package or central-version changes.
* `OIDC_AUDIENCE=https://build.invalid dotnet test
  src/Module.Profiles.Api.Test/VeloTime.Module.Profiles.Api.Test.csproj -c Release
  --no-build --no-restore`, without PostgreSQL opt-in:
  **160 passed / 28 skipped / 0 failed**.
* Same test with `PROFILES_TEST_POSTGRES_ADMIN_CONNECTION` targeting only our
  disposable server's `postgres` admin database: **208 passed / 0 skipped / 1 failed**.
  `ProductionHealthTests.MissingSchema_IsNotReady_ThenMigrationMakesReady_WithoutCreatingPreferences`
  fails at line 61 (expected 200 after migrations; got 503). Isolated rerun:
  **0 passed / 0 skipped / 1 failed**, same assertion. Profiles production/tests
  were not changed. Unresolved out-of-scope regression for Tester/Coordinator,
  **not** a Notifications data blocker or a passing full suite.
* `NOTIFICATIONS_PROBE_DB=<isolated connection> dotnet run --project
  /tmp/notifications-data-cancellation/Probe.csproj -c Release`:
  **33 cancellation + 56 core mutation/snapshot PostgreSQL checks passed**.
  Both real migrations applied; future empty intervals, unchanged start, earlier
  end, UTC/version/no-op, slot/revision/outbox state, precision readback/collapsed
  intervals/historical rounding, exact SQL audiences, separate login layers,
  fixed-duration retries/renewal, all restore scopes, policy tightening, eight
  concurrent first account/session writes, generation/reset/removal, >100 rows /
  actual UUID keysets, private-excluding whole-set deadlines, boundary expiry,
  encrypted identity/role/page/revision fences and dismissal/admin SQL projections.
* `OIDC_AUDIENCE=https://build.invalid NOTIFICATIONS_PROBE_DB=<isolated connection>
  dotnet run --project /tmp/notifications-api-probe/Probe.csproj -c Release`:
  **75 protocol/runtime + 22 real API/client/PostgreSQL checks passed**.
  Locally signed validated JWT + assertion signature/identity/token/audience/
  expiry/ticket cap, exact enums/bounds, no-store auth/admin errors, encrypted
  token rejection, typed Problems, real create/End/precision/stale version/dismiss/
  restore/active paging/revision rejection, safe reader projection and health.
  The 503 probe intentionally uses a separate unavailable database fixture.
* Discovery found no running PostgreSQL container/listener, with cached `postgres:17`.
  Created only disposable container `notifications-api-20261106-0951`, loopback
  random port `127.0.0.1:32768`, database `notifications_api_20261106_0951`,
  PostgreSQL **17.11**. No existing databases were mutated. Profiles fixtures
  created/dropped unique databases on this disposable server. Our database/container
  were removed after evidence collection; no test server/DB is left running.
  Final read-only evidence: both migration IDs in EF history, obsolete ordering
  check absent and finite-timestamp check retained; 128 notices, 1 account dismissal,
  0 session dismissals, 7 slots, 153 outbox hints and revision 153, including 4
  cancelled intervals. Inventory contained only our DB plus PostgreSQL defaults.
* `git diff --check`: passed. At the API-phase cut, worker dispatch/claim/restart and
  outbox failure injection had not yet run; see the integrated Tester results below.

## Durable API/core regression tests

`src/Module.Notifications.Api.Test` replaces the API/core temporary probes with
checked-in validation, cryptographic identity, real API authorization and PostgreSQL
service tests. Unit and identity tests run without PostgreSQL; every database test
opts in explicitly. The fixture accepts only a PostgreSQL admin connection to
`Database=postgres`, creates a GUID-named disposable database, applies the actual
migrations and drops only that database.

```sh
OIDC_AUDIENCE=https://build.invalid \
NOTIFICATIONS_API_TEST_ADMIN_CONNECTION='Host=127.0.0.1;Database=postgres;Username=postgres;Password=...' \
  dotnet test src/Module.Notifications.Api.Test/VeloTime.Module.Notifications.Api.Test.csproj \
  --configuration Release
```

The durable suite covers the four audience kinds and anonymous projection/privacy,
exact role and issuer/subject selection, default/override policy validation,
forbidden scopes, UTC start/end/snooze boundaries, dismissal-layer idempotency and
restore, concurrent application-login dismissals, policy tightening, future End
cancellation, paging/revision restart, transactional outbox rollback, BFF assertion
binding and the actual admin permission policy. It is not a full OIDC, broker or
browser delivery suite.

### Integrated Tester verification (2026-11-06)

* API/core against a disposable PostgreSQL 17 database: **16 passed, 0 skipped,
  0 failed** with Release and coverage settings.
* Existing Notifications processor against the same disposable PostgreSQL 17 server:
  **6 passed, 0 skipped, 0 failed** with Release and coverage settings.
* Focused Platform notification/server tests with the opt-in local Redis fixture:
  **38 passed, 0 skipped, 0 failed**. The two-host SignalR WebSocket path measured
  **42 ms** from bridge invocation to TestServer client receipt (2-second target).
  This bypasses Azure Service Bus and is not end-to-end browser delivery.
* `node --test src/VeloTime.Platform.Test/NotificationsBrowser.test.mjs`: **5 passed**.
  Chart migration/topology tests: **34 passed**; Helm lint/template checks passed.
* Full Release solution build succeeded with 69 repository warnings and 0 errors.
  Full Platform test run: **559 passed, 15 skipped, 1 failed**. The failure is
  unchanged `CiWorkflowTests.Definition_RunsMasterAndV2WithFacilitiesPostgresEnabled`:
  it expects `dotnet test --logger ...`, while the checked-in workflow invokes
  `dotnet test VeloTimer.sln --logger ...`. No CI or unrelated test was changed.
* The known opt-in Profiles readiness check was reproduced separately on PostgreSQL:
  `ProductionHealthTests.MissingSchema_IsNotReady_ThenMigrationMakesReady_WithoutCreatingPreferences`
  expected 200 after migration but received 503. It is unrelated and was not changed.
* No Azure Service Bus credentials or OIDC issuer runtime were configured. Playwright
  Chromium is available, but a full authenticated WebUI browser runtime could not be
  started. The live broker and full browser push acceptance remain unverified; no
  broker pass is inferred from the local backplane fixture.

Processor PostgreSQL claim/retry/deadline tests use a separate opt-in variable and
also create unique disposable databases:

```sh
NOTIFICATIONS_WORKER_TEST_ADMIN_CONNECTION='Host=127.0.0.1;Database=postgres;Username=postgres;Password=...' \
  dotnet test src/Module.Notifications.Processor.Test/VeloTime.Module.Notifications.Processor.Test.csproj \
  --configuration Release
```

UI/browser JavaScript regression tests:

```sh
node --test src/VeloTime.Platform.Test/NotificationsBrowser.test.mjs
```

Set a measurable local instant-delivery acceptance target of **at most 2 seconds**
from a refresh event becoming publishable in the outbox until each connected WebUI
replica invokes its client refresh callback. Record both server bridge receipt and
client callback timestamps; a successful unit/fixture test is not evidence that a
broker or multi-replica SignalR delivery met this target. Live Azure Service Bus,
two-replica Redis backplane, authenticated interactive-browser delivery and latency
remain deployment-level checks.

The full integrated phase still requires manual selection of Code Reviewer after
test validation. Coordinate the known Profiles production-readiness 503 with its
owner; it is unrelated and was not changed here. A full live broker/backplane/browser
acceptance is not claimed.
