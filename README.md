<div id="top"></div>
<!--
*** Thanks for checking out the Best-README-Template. If you have a suggestion
*** that would make this better, please fork the repo and create a pull request
*** or simply open an issue with the tag "enhancement".
*** Don't forget to give the project a star!
*** Thanks again! Now go create something AMAZING! :D
-->



<!-- PROJECT SHIELDS -->
<!--
*** I'm using markdown "reference style" links for readability.
*** Reference links are enclosed in brackets [ ] instead of parentheses ( ).
*** See the bottom of this document for the declaration of the reference variables
*** for contributors-url, forks-url, etc. This is an optional, concise syntax you may use.
*** https://www.markdownguide.org/basic-syntax/#reference-style-links
-->
[![Build pre-release](https://github.com/asbjorjo/VeloTimer/actions/workflows/development-release.yml/badge.svg?branch=v2)](https://github.com/asbjorjo/VeloTimer/actions/workflows/development-release.yml)
<br/>
[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![MIT License][license-shield]][license-url]
[![LinkedIn][linkedin-shield]][linkedin-url]



<h3 align="center">Velo Timer</h3>

  <p align="center">
    An application for turning data from a velodrome timing system into helpful or interesting statistics for riders and coaches.
    <br />
    <!-- <a href="https://github.com/asbjorjo/VeloTimer"><strong>Explore the docs »</strong></a>
    <br />--!>
    <br />
    <a href="http://velotimer.azurewebsites.net/">View Demo</a>
    ·
    <a href="https://github.com/asbjorjo/VeloTimer/issues">Report Bug</a>
    ·
    <a href="https://github.com/asbjorjo/VeloTimer/issues">Request Feature</a>
  </p>
</div>



<!-- TABLE OF CONTENTS -->
<details>
  <summary>Table of Contents</summary>
  <ol>
    <li>
      <a href="#about-the-project">About The Project</a>
      <ul>
        <li><a href="#built-with">Built With</a></li>
      </ul>
    </li>
    <li><a href="#roadmap">Roadmap</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#contact">Contact</a></li>
    <li><a href="#acknowledgments">Acknowledgments</a></li>
  </ol>
</details>



<!-- ABOUT THE PROJECT -->
## About The Project

The main purpose of this is to provide a way of turning passing data from the timing system at a velodrome into meaningful data and tools for coaches and riders.
An aside is to produce fun statistics for riders and track owners.

<p align="right">(<a href="#top">back to top</a>)</p>



### Built Using

* [ASP.NET Core](https://dotnet.microsoft.com/apps/aspnet)
* [EntityFramework Core](https://docs.microsoft.com/en-us/ef/)
* [Blazor](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
* [MudBlazor](https://mudblazor.com/)

<p align="right">(<a href="#top">back to top</a>)</p>

<!-- ROADMAP -->
## Publishing

Pushes to `v2` automatically run the
[Development Release workflow](.github/workflows/development-release.yml), which
calls the reusable [Publish workflow](.github/workflows/publish.yml) with the
`alpha` prerelease identifier. Versionize retains its
`--exit-insignificant-commits` behavior: pushes without significant commits do not
publish a new version and leave the rolling prerelease unchanged. Such runs stop
with Versionize's existing nonzero exit status.

Successful alpha builds publish NuGet packages to GitHub Packages and container
images to GHCR. The workflow pushes version commits and tags back to the branch
and updates chart image tags and `appVersion` to the published version. Packages
and images use unique versions such as `2.1.0-alpha.2`. After successful
publishing, the development workflow creates or updates the single
[rolling alpha prerelease](https://github.com/asbjorjo/VeloTimer/releases/tag/alpha)
and moves its `alpha` tag to the exact published commit. It is never marked as
the latest stable release. Failed publishes do not advance the rolling release.
Helm chart packages are not published by this workflow.

Development runs are serialized without cancelling active publishing; newer
pushes may replace pending runs. Workflow-generated pushes use `GITHUB_TOKEN`
and do not recursively trigger publishing. Repository policies must allow that
token to write packages, push version commits/tags, and move the `alpha` tag.

Manual Publish runs remain available: leave `prerelease` empty for the existing
stable publishing behavior or supply an identifier such as `alpha` or `beta`.
Successful manual Publish runs still trigger the
[GitHub Release workflow](.github/workflows/github-release.yml), creating
version-specific releases rather than updating the rolling `alpha` prerelease.
The GitHub Release workflow's compatibility changes must also be present on the
default branch, where GitHub evaluates its `workflow_run` trigger.

## WebUI Backend for Frontend

`WebUI.Mud` hosts the browser API and owns backend service discovery, aggregation,
Keycloak access, and token refresh. `WebUI.Mud.Client` uses the same service
interfaces, with same-origin HTTP implementations in the browser. Interactive Auto
uses the existing server implementations during prerendering/server execution and
the HTTP implementations when WebAssembly runs. Access and refresh tokens are not
serialized into browser authentication state.

Public reads:

* `GET /api/facilities`
* `GET /api/facilities/{facilityId}/layouts`
* `GET /api/layouts/{layoutId}`
* `GET /api/statistics/samples?cursor=...&isNextPage=false&pageSize=10`
* `GET /api/statistics/best-results?facilityId=...&from=...&to=...`

The front page shows one fastest public result per statistics item across all
facilities for the rolling last 30 days. Each row includes the statistics profile
name, average speed in km/h, elapsed time, and facility name. Speed and elapsed
seconds display three decimal places; both columns are right-aligned. Hidden profiles and
results without a linked statistics profile are excluded before selecting winners.
Result processing does not currently populate profile links automatically; without
existing public profile links, the table is empty.

The reusable `StatisticsTable` component accepts optional `FacilityId`, `From`, and
`To` parameters. Omit the facility for all facilities and omit time bounds for
all-time results. Bounds filter result completion time in UTC, with an inclusive
`From` and exclusive `To`; a supplied range must have `From < To`. Equal durations
are resolved by earliest completion time, then result ID. Items without eligible
results are omitted. The Statistics API exposes the same best-results route and
filters; the WebUI BFF resolves facility names without exposing backend services
to the browser. The table distinguishes loading, empty data, and retryable errors.

Cookie-authenticated operations:

* `GET /api/facilities/{facilityId}/installations`
* `GET /api/timing/installations` and `GET /api/timing/installations/{id}`
* `PUT /api/timing/installations/{id}` with an `InstallationView` JSON body
* `GET /api/profile/username` and `GET /api/profile/accounts`
* `POST /api/profile/accounts/{provider}/link` with a JSON string containing a local
  or same-origin redirect URI
* `DELETE /api/profile/accounts/{provider}`

API authentication failures return HTTP 401/403 instead of HTML login redirects.
Login, logout, password changes, and provider challenges remain full-page
`/authentication/...` operations. Before a browser mutation, the HTTP handler
fetches `GET /api/antiforgery`, then sends its JSON string token in `X-CSRF-TOKEN`.
Mutations without a valid token are rejected. BFF responses are marked `no-store`.
For upstream mutations that require no response body, the BFF accepts any HTTP
200–299 status while preserving its existing browser-facing response codes.
Calls requiring a DTO still require a valid response body. Non-success responses
continue through the existing error handling.

Run the application through the Aspire AppHost for service discovery and its
Redis, Keycloak, and domain API dependencies. Focused boundary tests run with:

```sh
dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --filter FullyQualifiedName~WebUiBffTests
```

## Administration

The personal and administrative transponder tables use server-side pagination of
ownership rows (not distinct transponders). Pages default to 10 rows, with 10/25/50
available in the UI and a server maximum of 100. Include history is enabled by
default; disabling it retains current ownerships and genuinely unassigned
administrative transponders. Search and history changes restart at the first page.
The add-ownership transponder selector also searches and loads bounded pages.

**Breaking API change:** the Timing `GET /api/timing/transponders` and
`GET /api/timing/my/transponders` endpoints, and the corresponding WebUI BFF
`GET /api/admin/timing/transponders` and `GET /api/transponders` routes, return
`{ items: [{ transponder, ownership }], totalCount }` rather than an array of
transponders. They accept zero-based `page`, `pageSize`, `search`, and
`includeHistory` parameters. Each row contains at most one ownership;
`transponder.ownerships` is empty. The total counts matching ownership rows before
paging. Invalid page sizes or offsets return HTTP 400; out-of-range pages are empty.

The WebUI search box preserves case-insensitive label, system-ID, and displayed
owner-name matching, including one-character queries and fallback names.
For nonempty searches the BFF streams compact candidate metadata and resolves
owner identities in bounded server-side batches before requesting the matching
database page. Broad owner-name searches can therefore be slower, but no complete
transponder/ownership graph is loaded upfront. Ordinary paging resolves only the
displayed owners and computes administrative LastSeen only for the current page.
Direct Timing list searches match labels and system IDs; the BFF composes owner
names without sharing databases between services.

The administration screens replace the previous admin pages and share the existing
Interactive Auto/BFF infrastructure. Navigation and direct URLs enforce module roles.

* `/admin/facilities` manages facilities; each facility links to its course layouts.
  The layout editor manages named coursepoints, ordered connected segments, lengths
  in metres, reuse of points within a facility, and timing-point links.
* `/admin/timing/installations` manages installations and their facility assignments.
  Each installation includes timing-point management and a coursepoint selector.
  The previous `/admin/installations` URLs remain aliases. Observed hardware identity
  and recorded passings are protected from incompatible edits.
* `/admin/statistics/items` and `/admin/statistics/configurations` manage statistics
  items, Simple coursepoint configurations, and Multi repetition configurations.
  Definitions with dependencies cannot be deleted. Recorded results prevent changes
  to timing semantics; create a new definition instead. Descriptive edits are allowed.
* `/transponders` lets a signed-in user register a MyLaps X2 code, list their own
  ownership periods, end an active period, or permanently delete a selected period.
  Registration has an editable start date (today by default) and an optional end
  date. These are browser-local calendar days: the entire end day is included,
  including across daylight-saving changes. A blank end means no expiry.
  The read-only owner label uses the signed-in user's Statistics profile name,
  including their own private profile. Keycloak names are never returned by the
  personal owner-name endpoint.
* `/admin/timing/transponders` lets timing administrators register transponders, edit
  their labels, delete unused transponders, and create, edit, end, or delete anyone's
  ownership periods. Owners are selected by Statistics profile name or Keycloak
  first/last name rather than by entering a UUID. Administrator labels show both
  names, including private Statistics profiles; UUIDs are secondary disambiguation
  details only. Ownership editor timestamps
  are UTC. Registration uses the same local calendar dates as personal registration
  and requires selecting an owner. Deleting a transponder is blocked by ownership,
  passings, or statistics.

Registering an already-owned transponder updates overlapping periods for that owner
instead of duplicating them. Extending coverage is allowed if no other owner's period
conflicts. Shortening coverage requires confirmation showing the existing and proposed
dates; cancelling keeps the existing ownership unchanged. If ownership changes while
the confirmation is open, it must be reviewed again. Separate periods are retained as
history. Personal registration always assigns the authenticated user; only timing
administrators can select another owner.

Owner search starts after two characters and returns up to 20 results per page,
with **Load more owners** for additional matches. Select an actual search result;
typed text alone does not choose an owner. `No statistics profile` means a user
has not created a Statistics profile and does not block registration.
`User unavailable` marks a historical owner whose Keycloak account was deleted;
the ownership is retained and can be edited without reassigning it. Dependency
outages and insufficient permissions produce an explicit retryable error instead
of being displayed as missing profiles or users.

All relationship selections are validated again by the owning API. Facilities,
layouts, installations, timing points, and statistics definitions cannot be deleted
while dependent records remain. Cross-module dependency checks use authenticated
HTTP, never another module's database. If a required service is unavailable, the
operation fails closed. These preflight checks are not a distributed transaction;
local transactions and database constraints protect local invariants.

Coursepoints may be shared between layouts in the same facility. Timing links must
remain within that facility, and a timing point can link to only one coursepoint.
Layout geometry used by statistics definitions or recorded samples cannot be changed;
create a new layout instead. Relationship cache entries are invalidated after edits.
Layout or timing-point saves that succeed before a separate link operation fails
report the partial save explicitly and retain the saved ID for retry.

Keycloak realm roles are mapped at authentication into explicit `velotime_permission`
claims. The `velotime-admin` permission grants all modules; `facilities-admin`,
`timing-admin`, and `statistics-admin` grant their respective modules. Timing admins
also receive the scoped coursepoint/timing-point linking permission, without broader
Facilities write access. APIs, BFF routes, pages, and navigation all use named
permission policies; they do not authorize with role checks. Ordinary accounts do not
receive administrator permissions automatically.

Statistics profile mutations (`POST /api/statistics/profiles` and
`PUT`/`DELETE /api/statistics/profiles/{userId}`) require the `statistics-admin`
or `velotime-admin` realm role through the Statistics administrator permission
policy. A `Statistics.Write` scope alone no longer grants write access.
Profile list and detail reads remain available to authenticated callers.
The users-migration service account must include `statistics-admin` (or
`velotime-admin`) in its access token's `realm_access.roles`; assigning a role
in Keycloak is insufficient if client scope mappings exclude it from the token.
Obtain a fresh service-account token after changing role or scope mappings.

For a new realm, the checked-in import declares these roles and includes realm roles
in the WebUI ID token. Existing Keycloak realms are not updated by editing the import:
create the roles, add the WebUI realm-role mapper to ID/access tokens, assign the
appropriate role to administrators, then sign out and sign in again. No admin API
credentials are sent to the browser.

Transponder owner search additionally uses the signed-in administrator's delegated
access token against the Keycloak Admin API; no service-account secret is needed.
The development realm makes `timing-admin` and `velotime-admin` composite roles
with the **realm-management** client roles `query-users` and `view-users` only.
For existing/production realms, add those same read-only composite roles. Do not
grant `manage-users` or broader realm administration for this feature.
Ensure the WebUI token scope includes those client roles in
`resource_access.realm-management.roles` and resolves the `realm-management`
audience (the checked-in realm uses the client-role and audience-resolve mappers
and full scope on `velotime.webui`). If client scope is restricted in production,
explicitly include these read-only roles. Sign out and sign in after changing
role/scope assignments. Name lookup failures with 403 indicate that these
permissions or token scope need checking; the application does not silently
fall back to raw UUIDs.

The `velotime-web` client scope is a default scope in the checked-in realm import. It
adds the application URL audience and user subject required by the domain APIs, as
well as the realm-role claims used by the WebUI. The scope does not grant
administrator roles or bypass ownership checks. Existing realms need the scope
assigned to clients and users must sign out and back in after scope assignments
change so newly issued tokens contain the claims.

### Transponder Ownership Events

The Timing API queues [TransponderOwnershipChanged](src/Module.Timing.Interface/Messages/TransponderOwnershipChanged.cs)
in a PostgreSQL transactional outbox within the same transaction as the ownership
change. Either both commit or neither does. A committed request does not wait for
Azure Service Bus delivery, so broker outages do not prevent saving ownership.
Serialization/outbox persistence failures roll back the mutation and return HTTP
500; they are not reported as ownership conflicts.

Each event contains a stable `EventId`, `TransponderId`, and a `Changes` list
ordered by ownership record ID. Each change includes `Before` and `After`
snapshots with the record ID, transponder ID, owner ID, inclusive UTC start,
and optional exclusive UTC end. Creation has a null `Before`; deletion has a null
`After`. Registration/reconciliation, claiming, admin creation/editing, ending,
deletion, and the legacy add-owner endpoint queue one event per changed operation,
including historical and future periods. Reconciliation groups updates and
deletions into one event. No event is queued for unchanged saves, repeated
claims/registrations, confirmation previews, rejected requests, or failed
transactions. Reaching a period's start or end time does not itself queue an event.

The API host dispatches committed messages using SlimMessageBus's PostgreSQL
DbContext outbox integration. Messages go to the existing `velotime-timing-test`
topic as JSON, with `MessageName = TransponderOwnershipChanged`, the transponder
ID as `SessionId`, and `EventId` as `MessageId`. The topic must already exist;
the API does not provision Service Bus topology. Configure
`ConnectionStrings__MessageBus` for the Timing API. Aspire provides the
`MessageBus` reference; Helm exposes `secrets.MessageBus`. With `existingSecret`,
include the `ConnectionStrings__MessageBus` key in that secret.

Delivery is **at least once**: consumers must be idempotent or deduplicate by
`EventId`, which is preserved through retries and restarts. The Statistics
processor reconciles profile links against current ownership, making repeated
deliveries and replayed snapshots safe without a processed-event table. The outbox
preserves its sequence across API instances and dispatches one event at a time
to prevent later ownership events overtaking a failed event. A blocked event can
therefore delay all ownership events. This does not order ownership events
relative to independently published passing/sample events. Idle polling is every
five seconds; the Service Bus client's retry policy applies to transport sends.
The library delivery-attempt limit is set to `int.MaxValue` rather than the
three-attempt default so ordinary outages do not exhaust retries.

#### Statistics profile assignment

The Statistics processor consumes ownership changes on `velotime-timing-test`,
subscription `statistics`, alongside `TimingSampleComplete`. If subscription
filters are managed externally, allow both message types (`MessageName`) and
retain session support. Before/after periods identify impacted entries, including
entries losing coverage after an ownership is shortened, moved, or deleted.
The processor reads all current ownership periods from
`GET /api/timing/ownerships/{transponderId}`; it does not apply stale event
snapshots as the desired state or access the Timing database.

An entry belongs to an owner only when one ownership period contains the entire
entry: `TimeStart >= OwnedFrom`, `TimeStart < OwnedTo`, and `TimeEnd <= OwnedTo`
for a bounded period. An entry ending exactly at the ownership end is included;
one starting at that instant is not. Open-ended periods have no upper bound.
Entries spanning ownership boundaries have no profile link. The processor clears
stale links but does not recalculate durations, speeds, or statistics definitions.
Simple and multi-entry creation use the same assignment rules, including when
the ownership event arrived before the entry existed.

Owners map to `StatsProfile.UserId`. Missing profiles are automatically created
with the display name `Rider {owner GUID}` and **`IsPublic = true`**. This makes
their eligible results visible in public statistics. Existing profile names and
privacy settings are preserved. No Keycloak user-name lookup is performed.

The processor serializes creation/reconciliation per transponder using a
Statistics-owned PostgreSQL transaction advisory lock, with ordered owner locks
to prevent duplicate profile creation across transponders/replicas. Profile
updates and entry creation commit together, before downstream entry messages are
published. Ownership changes in Timing remain independently transactional and
converge through subsequent events. Timing lookup failures fail processing for
normal message retry, rather than clearing links as if ownership were empty.

The read endpoint requires `timing.ownership.read` (or existing Timing/global
administration permission). The checked-in realm assigns the narrow
`statistics-processor` realm role to the
`velotime.statistics.processor` service account. For existing realms, create that
role and assign it to the processor service account; include realm roles and the
application audience in its access tokens. This role does not grant ownership
mutations or Timing administration. Configure the processor's existing
`CLIENT_SECRET` and Keycloak connection, plus Timing service discovery.
Aspire supplies the Timing API reference/readiness dependency; Helm supplies
`services__module-timing-api__http__0`. No Statistics schema migration is required.
This is not a blanket historical backfill: unaffected historical entries are
updated only by a relevant ownership event.

PostgreSQL ownership/concurrency tests create and drop uniquely named isolated
databases. Set `VELOTIME_TEST_STATISTICS_POSTGRES` to a test connection with
database create/drop privileges, then run:

```sh
OIDC_AUDIENCE=https://test.invalid dotnet test src/VeloTime.Workers.Test/VeloTime.Workers.Test.csproj --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
```

Run the existing Timing EF migration worker before starting the API so the
`timing` schema exists. At API startup, the library applies its supported outbox
migrations in that schema, creating `timing.smb_outbox`,
`timing."__EFMigrationsHistory"`, indexes, and its locking function. These are
separate from EF's module migration history in the `ef` schema; no EF model change
is needed. The API database role needs schema `USAGE`/`CREATE`, permission to
create the outbox tables/indexes/function, and read/write/delete/execute access
to those objects. Startup waits for outbox initialization and fails explicitly
if it cannot complete. Build-time OpenAPI generation skips messaging startup.

Delivery failures are logged by SlimMessageBus. Inspect `timing.smb_outbox`:
`delivery_complete = false AND delivery_aborted = false` is the pending backlog;
`timestamp` shows its age, `delivery_attempt` shows attempts, and
`delivery_aborted = true` requires operator investigation (for example an
unresolvable event type after a deployment). Keep event contracts compatible
across replicas and restarts. Fix the underlying cause before requeueing an
aborted message; do not generate a replacement event ID. Delivered messages older
than three days are cleaned hourly in batches of 100. Pending and aborted events
are not removed by that cleanup.

Outbox integration tests use real PostgreSQL and the real SlimMessageBus dispatcher
with a controlled Azure sender, without a live broker. Set
`VELOTIME_TEST_OUTBOX_POSTGRES` to a test PostgreSQL connection whose role can
create/drop databases, then run:

```sh
OIDC_AUDIENCE=https://test.invalid dotnet test src/Module.Timing.Api.Test/VeloTime.Module.Timing.Api.Test.csproj --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
```

Each outbox test uses a uniquely named temporary database and removes it afterward.
Without that variable the PostgreSQL tests are explicitly skipped, not replaced by
SQLite atomicity tests. CI supplies an isolated PostgreSQL service and runs them.

### Access-Token Audience Configuration

Set `OIDC_AUDIENCE` to the public application base URL in all three domain APIs,
for example `https://development.veloti.me` for development or
`https://veloti.me` for production. Set the `velotime-audience` mapper's Included
Custom Audience in the `velotime-web` client scope to the same URL. Direct API startup fails if this setting is
missing, blank, or not an absolute HTTP(S) URL without credentials, query, or
fragment. The audience is matched exactly, including any trailing slash; it is
not inferred from the API listener, request host, or Keycloak issuer.

Only local Aspire runs in the Development environment supply
`https://development.veloti.me` when `OIDC_AUDIENCE` is absent. AppHost passes the
same value to Keycloak and all three APIs. An explicitly configured value takes
precedence; a blank or invalid value is rejected. To override it locally:

```bash
OIDC_AUDIENCE=https://preview.example dotnet run --project src/AppHost
```

There is no production default, API-level default, or published AppHost parameter
default. Published deployments must supply the required `oidc-audience` parameter.
For direct API deployments, explicitly set `OIDC_AUDIENCE` on each API process.
The checked-in realm import uses the development URL; update its audience mapper
when configuring a different environment.

The local Aspire realm import uses the `StatisticsProcessorSecret`,
`TimingProcessorSecret`, and `UserMigrationSecret` AppHost parameters for both
Keycloak's service-account clients and the matching worker processes. Configure
these secret parameters to the same values when running AppHost.

Existing realms are not updated by editing the import or restarting Keycloak.
In the `velotime-web` client scope, edit the `velotime-audience` mapper: set
**Included Custom Audience** to the same URL configured in the APIs. Keep
access-token inclusion enabled and ID-token inclusion disabled. Update equivalent
service-account mappers/scope assignments if applicable. Coordinate this change
with the API deployment: `velotime.api` is no longer accepted.
Sign out and back in, and obtain fresh service-account tokens after the change.
WebUI client IDs, ID-token validation, and administrator permissions are unchanged.

The application, bootstrap, and users-migration Helm charts accept an optional
`global.oidcAuthority` containing the full Keycloak base URL, such as
`https://identity.example.com`. When configured, each VeloTime workload's ConfigMap
sets `services__keycloak__https__0` to that exact URL and
`ConnectionStrings__keycloak` to the URL followed by `/realms/velotime`, removing
trailing slashes before appending the realm path. The default is empty and omits
both settings. Configure the value separately for each installed chart; it does
not replace the API audience setting.

Build-time OpenAPI generation starts the API hosts and therefore also requires an
explicit audience. CI and package/container build jobs configure `https://build.invalid`
only for this tooling; this value is not an application default or container runtime
setting. For local solution builds/tests without another configured value:

```bash
OIDC_AUDIENCE=https://build.invalid dotnet build --configuration Release
OIDC_AUDIENCE=https://build.invalid dotnet test --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
```

The browser-local registration date helper also has dependency-free JavaScript tests:
`node --test src/VeloTime.Platform.Test/RegistrationDates.test.mjs` (Node.js 22+).

Ownership mutations use a PostgreSQL transaction advisory lock per transponder and
the existing unique system/SystemId index. Self-service endpoints derive the owner
from the authenticated subject; they do not accept an owner ID. Ending retains the
ownership period; deleting removes only that period, not the transponder or results.

The Timing and Facilities API test factories can target a **disposable test database
only** using `VELOTIME_TEST_POSTGRES`. Their setup deletes and recreates that database;
run these suites separately when sharing a test database. With this setting,
competing-claim tests run concurrently against real PostgreSQL and link mutations
exercise PostgreSQL transaction locks. The default SQLite test run checks the same
ownership conflict sequentially.

## Roadmap

- [] User authentication
    - [] Authorization
    - [] Strava account linking
    - [] Social media account linking

See the [open issues](https://github.com/asbjorjo/VeloTimer/issues) for a full list of proposed features (and known issues).

<p align="right">(<a href="#top">back to top</a>)</p>



<!-- CONTRIBUTING -->
## Contributing

Contributions are what make the open source community such an amazing place to learn, inspire, and create. Any contributions you make are **greatly appreciated**.

If you have a suggestion that would make this better, please fork the repo and create a pull request. You can also simply open an issue with the tag "enhancement".
Don't forget to give the project a star! Thanks again!

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

<p align="right">(<a href="#top">back to top</a>)</p>



<!-- LICENSE -->
## License

No license yet. See `LICENSE.txt` later.

<p align="right">(<a href="#top">back to top</a>)</p>



<!-- CONTACT -->
## Contact

Asbjørn L. Johansen - [@asbjorjo](https://twitter.com/asbjorjo) - asbjorjo@gmail.com

Project Link: [https://github.com/asbjorjo/VeloTimer](https://github.com/asbjorjo/VeloTimer)

<p align="right">(<a href="#top">back to top</a>)</p>



<!-- ACKNOWLEDGMENTS -->
## Acknowledgments

* [Best-README-Template](https://github.com/othneildrew/Best-README-Template)

<p align="right">(<a href="#top">back to top</a>)</p>



<!-- MARKDOWN LINKS & IMAGES -->
<!-- https://www.markdownguide.org/basic-syntax/#reference-style-links -->
[contributors-shield]: https://img.shields.io/github/contributors/asbjorjo/VeloTimer.svg?style=for-the-badge
[contributors-url]: https://github.com/asbjorjo/VeloTimer/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/asbjorjo/VeloTimer.svg?style=for-the-badge
[forks-url]: https://github.com/asbjorjo/VeloTimer/network/members
[stars-shield]: https://img.shields.io/github/stars/asbjorjo/VeloTimer.svg?style=for-the-badge
[stars-url]: https://github.com/asbjorjo/VeloTimer/stargazers
[issues-shield]: https://img.shields.io/github/issues/asbjorjo/VeloTimer.svg?style=for-the-badge
[issues-url]: https://github.com/asbjorjo/VeloTimer/issues
[license-shield]: https://img.shields.io/github/license/asbjorjo/VeloTimer.svg?style=for-the-badge
[license-url]: https://github.com/asbjorjo/VeloTimer/blob/master/LICENSE.txt
[linkedin-shield]: https://img.shields.io/badge/-LinkedIn-black.svg?style=for-the-badge&logo=linkedin&colorB=555
[linkedin-url]: https://linkedin.com/in/asbjorjo
