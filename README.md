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

The independent Profiles service stores signed-in language preferences in its
own schema in the existing `velotimedb` PostgreSQL database. It is separate from
Statistics display-name profiles and Keycloak account linking. Its authenticated API exposes
`GET /api/profiles/me/preferences` and
`PUT /api/profiles/me/preferences/language`; saved values are exactly `en` or
`nb-NO`, and a missing preference is returned as `null` without creating a row.
See [Profiles persistence and deployment](src/Module.Profiles/README.md) for
database, migration ordering, Helm Secret, and API deployment requirements.

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

Administrative provisioning can use `POST /api/statistics/profiles/ensure`
(`EnsureStatsProfile`) with the existing create-profile request shape. It returns
`201 Created` with a profile Location header when it creates a missing profile, or
`200 OK` with the saved profile **unchanged** when one already exists. The requested
name and privacy are creation defaults only. Both ensure and ordinary profile
creation serialize with Statistics processor fallback creation using the same
per-user PostgreSQL transaction lock. Ordinary POST creation still returns `409`
for an existing profile; explicit PUT updates remain available to administrators.
Creation and ensure reject an empty user ID or blank profile name.

#### Legacy ownership migration

The users-migration service account also needs `timing-admin` (or `velotime-admin`)
in its access token. The checked-in `velotime-users-migration` composite role grants
both Timing and Statistics administration; existing realms must be updated separately.

Before any ownership writes, the worker validates legacy periods and groups them by
rider and canonical X2 transponder ID. It merges duplicates, overlaps, touching
periods, and gaps **strictly less than 15 minutes** into continuous coverage.
A gap of exactly 15 minutes stays separate. Open-ended periods remain open-ended.
Different riders and transponders are never merged. Invalid groups and cross-owner
conflicts (including another owner's coverage inside a merged gap) are reported
and withheld before any ownership writes; independent groups can proceed.

UTC start timestamps are preserved without calendar-day truncation. Legacy finite
ends were inclusive; the worker adds **one PostgreSQL microsecond** to convert
each merged end to the registration API's exclusive-end convention. It does not
add a day. Gap comparisons use the original timestamps before this conversion.

Each normalized period is submitted to the administrative ownership registration
endpoint, not the append-only ownership creation endpoint. Identical retries are
no-ops. Conflicts and HTTP 200 responses requiring shortening confirmation are
reported for manual review; the worker never automatically confirms shortening.
Expected registration validation/conflict failures do not prevent independent
registrations, but unresolved issues fail the run with a nonzero exit code after its
summary. Cancellation, authentication/authorization errors, and unexpected server
failures stop immediately.
There is no global transaction: completed registrations remain saved and are safe
to retry. This is not a general cleanup of existing destination ownership records.

The worker also ensures each migrated user's Statistics profile rather than
overwriting an existing profile after a create conflict. It compares the returned
name exactly (including case and whitespace) and checks privacy against legacy
values. Differences are logged as unresolved migration conflicts, never silently
updated. This includes processor-generated `Rider {userId}` placeholder names.
Even when a profile differs, that rider's ownership registrations and later riders
continue. Profile and ownership issues are combined into the final failure with
a nonzero exit code. Resolve preference differences explicitly before rerunning;
matching existing profiles are successful retries. Missing identity accounts are
reported as unresolved profile migrations. If the identity account lookup throws,
the error is logged, that rider is skipped, and its profile and ownership work is
reported as unresolved.

For a new realm, the checked-in import declares these roles and includes realm roles
in the WebUI ID token. Existing Keycloak realms are not updated by editing the import:
create the roles, add the WebUI realm-role mapper to ID/access tokens, assign the
appropriate role to administrators, then sign out and sign in again. No admin API
credentials are sent to the browser.

Transponder owner search additionally uses the signed-in administrator's delegated
access token against the Keycloak Admin API; no service-account secret is needed.
The development realm makes `timing-admin`, `facilities-admin`, `statistics-admin`,
and `velotime-admin` composite roles
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

### Transponder Status

Click a transponder label in the personal or administrative list, or a transponder
ID in sample results, to open `/transponders/{id}`. The read-only status page shows
the editable label separately from the hardware code and system ID, ownership
history, and the last recorded sighting. Locations identify the facility,
installation, and timing point; timestamps are UTC. An installation without a
linked facility is explicitly marked **Unassigned facility**.

All four administrator roles can inspect any transponder and its complete ownership
history, including Statistics profile and Keycloak owner names. Other signed-in users
must currently own the transponder. They see only their own ownership periods and
sightings within those periods, including previous periods when they owned it.
Ownership starts are inclusive and ends exclusive. Every refresh or page request
checks access again; former and future owners cannot use this page. Historical-only
personal list entries remain plain text unless the viewer has administrator access.
The existing Timing administration pages, owner search, and mutation permissions
are unchanged. Only bounded read-only owner-name resolution is available to the
additional administrator roles. Existing Keycloak realms must receive the read-only
composite-role updates described above, followed by a fresh sign-in.

Battery, signal-strength, and hit-count warnings are separate hardware-reported
flags. The page lists every flagged passing in the last seven days, newest first,
with its exact time and location; a passing may have multiple warnings. Summary
indicators cover the entire visible window, not just the current page. Ownership
history and warnings have independent pagination. **No warnings reported** is not
a health guarantee. Older sightings still determine last seen, while future-dated
sightings do not contribute to current status.

The authenticated Timing endpoint is
`GET /api/timing/transponder-status/{id}`; the same-origin WebUI BFF route is
`GET /api/transponder-status/{id}`. Both accept zero-based `ownershipPage`,
`warningPage`, and `pageSize` (default 10, maximum 100). The Timing response includes
identity, the latest visible sighting, the warning window, three warning indicators,
`ownerships`/`ownershipCount`, and `warnings`/`warningCount`. The BFF additionally
resolves facility names. Private responses are not cached. Missing or inaccessible
transponders return 404 for signed-in non-admins; invalid user subjects return 403.
Name lookup failures are explicitly retryable and do not hide already loaded status.

Concrete hardware agents must upgrade their `VeloTime.Agent` dependency and populate
`LowBattery`, `LowStrength`, and `LowHits` on each passing. Hardware decides what is
low; no numeric thresholds are introduced here. Forwarding preserves the passing
timestamp and loop, so warning locations are exact. Apply the new agent
`AddPassingLowHits` EF migration before using the upgraded agent passing store.
The Timing database already contains all three flags and needs no new migration.
The additive event properties preserve the existing passing constructor and
deconstruction; older event payloads remain readable with absent flags false.
Previously dropped warnings cannot be backfilled, and the separate
`TransponderStatusEvent` is not used because it lacks an exact loop and hit warning.

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

The same variable enables Statistics profile API concurrency tests:

```sh
OIDC_AUDIENCE=https://test.invalid dotnet test src/Module.Statistics.Api.Test/VeloTime.Module.Statistics.Api.Test.csproj --filter FullyQualifiedName~StatsProfile --logger "trx"
```

These tests verify concurrent ensure/create requests and API/processor profile-lock
interoperability against real PostgreSQL, using unique disposable databases. CI
sets the variable for its isolated PostgreSQL service. Without it, PostgreSQL tests
are explicitly skipped; ordinary endpoint tests still run on SQLite.

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

## Helm schema migrations

Install or upgrade the **application `velotime` chart** to run Facilities,
Statistics, Timing, and Profiles schema migrations as release-scoped Kubernetes
Jobs. These Jobs have `pre-install,pre-upgrade` hooks in the same release as the
application Deployments. Helm waits for each Job to complete before applying the
ordinary release resources, even without `--wait`. A failed Job or hook timeout
fails the operation before the Deployment updates. Application pods no longer
run migration init containers on restart or scale-out.

The four Jobs run sequentially in Facilities, Statistics, Timing, Profiles order.
All currently use `ConnectionStrings__velotimedb` and their service-owned schemas.
This hook ordering does not coordinate separate releases, external migration
runners, or concurrent database clients. Module subcharts are implementation
details of the application chart; deploy the application chart to obtain its
migration gate.

### Configuration and credentials

For each domain, `migrationJobs.domains.<domain>` can override:

- `enabled`: explicit true/false takes precedence over consumer settings.
  Otherwise a Job runs if any API/processor consumer has `migration.enabled: true`.
  Statistics and Timing each get one Job, not separate API/processor Jobs.
- `existingSecret`: a pre-provisioned Secret containing
  `ConnectionStrings__velotimedb`. By default this is the API's `existingSecret`.
  Set it to `""` to render a migration-only Secret from database values.
- `database`: partial overrides of the API's `database` values, including optional
  `name` (default `velotimedb`). Only used when no existing Secret is selected.
- `image.pullPolicy`: defaults to the API's `migration.image.pullPolicy`.

**API configuration is authoritative by default, even if only the processor
requests migration.** If the API and processor target different databases, choose
the intended migration target explicitly; the chart cannot inspect externally
managed Secret contents or migrate multiple targets from a single domain Job.
Global image repository/tag and image-pull Secrets apply to migration Jobs too.
OIDC configuration follows the API/global values, using a migration-only ConfigMap.

For example, separate a migration identity from runtime access:

```yaml
module-profiles-api:
  existingSecret: profiles-runtime
migrationJobs:
  backoffLimit: 1
  activeDeadlineSeconds: 600
  domains:
    profiles:
      existingSecret: profiles-schema-migration
```

Both external Secrets must already exist. All four APIs also support
chart-managed credentials through their existing `database` settings and an empty
`existingSecret`. Ordinary application Secrets retain normal Helm ownership.
Migration-only Secrets contain only the database connection string and are
created before the Jobs using the current operation's values, not the old
release's configuration.

The database, network access, image-pull credentials, and external Secrets must
be available **before** install/upgrade starts. A pre-install Job cannot use
ordinary resources that this same release has not created yet. Do not disable
hooks (`--no-hooks`) when depending on this migration gate. If a deployment
controller merely renders chart YAML, verify its own hook execution/ordering
semantics; annotations alone do not enforce the Helm lifecycle.

### Timeouts, diagnostics, and cleanup

`migrationJobs.backoffLimit` defaults to `1` (zero is supported);
`migrationJobs.activeDeadlineSeconds` defaults to `600` per Job. Size Helm's
`--timeout` to accommodate the expected hook duration and the chosen Job
deadlines/retries; it must not expire before an otherwise healthy migration
completes. Migration executables exit nonzero on failure, including interrupted
migrations. Retries must tolerate migrations that were already partially or
fully applied; execution is not an exactly-once guarantee.

Successful hook Jobs are deleted. Failed Jobs remain for `kubectl logs` and
`kubectl describe` until the next invocation replaces them. Migration hook
Secrets/ConfigMaps use `before-hook-creation` only: they remain between runs and
are replaced before the next enabled hook execution. They must not use
`hook-succeeded`, which would delete them immediately after creation and before
the Jobs consume them.

Hook configuration is **not automatically removed by Helm uninstall**. After
uninstall, remove retained credential/configuration objects explicitly, scoped to
the namespace and release:

```bash
kubectl -n <namespace> delete secret,configmap \
  -l 'app.kubernetes.io/instance=<release>,velotime.io/migration-config=true'
```

If a domain is disabled or switched to an external Secret, its old hook
configuration can remain. After ensuring no migration Job is using it, use the
same selector with `,velotime.io/migration-domain=<domain>` to remove only that
domain's retained configuration. Cleanup never targets externally managed
Secrets. Account for this retained credential material in operational procedures.

### Bootstrap, upgrades, and rollback

The separate bootstrap and legacy users-migration releases retain their existing
behavior and do not automatically gate an application upgrade. Bootstrap still
runs its configured migration init-container chain before seeding. Operators can
set the bootstrap chart's existing `migrations: []` option only when schemas are
guaranteed to exist before its seed step. Do not run independent migration
releases concurrently and assume application hook weights will coordinate them.

Old application pods remain active during pre-upgrade migrations. Use
backward-compatible expand/contract schema changes and test the previous
application version against the new schema. Helm rollback/automatic rollback
reverts Kubernetes resources, **not database changes**, and hook side effects are
not transactional with the release. Rolling back to an older chart may restore
its per-pod init containers against the already-upgraded database.

Chart regression tests exercise rendered install/upgrade resources:

```bash
python3 -m pip install -r charts/tests/requirements.txt
python3 -m unittest discover -s charts/tests -v
```

Run the chart dependency updates from [Charts CI](.github/workflows/charts-ci.yml)
first. These tests do not replace a disposable-cluster acceptance test: exercise a
fresh install, no-op/pending upgrade, failed migration, timeout, and retry; confirm
failed pre-upgrade hooks leave application Deployment image/spec updates unapplied.

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
