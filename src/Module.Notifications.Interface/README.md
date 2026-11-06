# Notifications v1 contracts (contract phase only)

`VeloTime.Module.Notifications.Interface` is the single shared contract owner for API,
identity and invalidation artifacts. There is no API, storage, client, handler, topology
or UI implementation in this phase. Namespaces follow existing `Interface.Data` and
`Interface.Messages` patterns. JSON uses camelCase properties, explicit string enum
codes (numeric enums rejected), ISO-8601 UTC timestamps and integer SI seconds.
Unrecognized codes, zero/undefined enum values, absent required fields and null
required collections/objects are validation failures, not implicit defaults.
Serializer acceptance alone is not domain validation.

## Ownership and authoring

A dedicated Notifications service exclusively owns notices, account dismissals,
session dismissals, scheduling and transactional outbox, in its own PostgreSQL schema.
Profiles tables and foreign databases are not queried. Initially only authorized
manual authoring invokes the core creation service. No generic public command API,
source-event subscriptions, inbox, recipes or speculative producer fields.

Content is plain text in both `English` (en) and `NorwegianBokmal` (nb-NO);
the UI selects a locale and formats dates/seconds. Both titles and bodies must be
nonblank, at most 200/4000 .NET string characters respectively. No markup rendering.
Version starts at 1 and increases on admin mutation. Presentation version supports
reconciliation, not revival: dismissals remain attached to the UUID across edits.
Materially new messages need new UUIDs. No recipient IDs, audience, author identities,
source metadata or admin timestamps occur in reader DTOs.

### Audience (admin DTO only)

| Kind | Required | Must be null |
|---|---|---|
| everyone | nothing | role, recipientIssuer, recipientSubject |
| authenticated | nothing | role, recipientIssuer, recipientSubject |
| role | nonblank role | recipientIssuer, recipientSubject |
| user | nonblank recipientIssuer AND recipientSubject | role |

`everyone` includes anonymous and authenticated readers. Roles are exact names from
validated token claims, not permissions supplied by callers. User targets match the
exact **issuer AND subject**, ordinal/case-sensitive, without trimming, URI
normalization or case folding. Unused fields are null, never empty strings. Only
authorized authors can select recipients; reading/dismissing never accepts identity.

### Effective dismissal policy

Null/omitted create policy selects this complete policy, persisted at creation:

| Severity | Allowed scopes | Default | DurationSeconds |
|---|---|---|---|
| info | session, permanent | permanent | null |
| warning | duration, session, permanent | duration | 86400 |
| error | duration, session, permanent | duration | 3600 |

Info has no arbitrary snooze duration: authors may enable duration by supplying an
explicit complete override. All other permitted choices remain available regardless
of severity. AllowedScopes must be nonempty, unique, known enum values; DefaultScope
must be a member. If duration is allowed, DurationSeconds is required in
**[1, 31536000]** (365 days). Otherwise it must be null. A complete override may
prohibit permanent; info then needs an explicit permitted alternative default.
No non-dismissible mode initially: authenticated policy always has at least one
scope. Anonymous public notices always permit browser-tab dismissal regardless of
the authenticated policy, including when permanent is prohibited.

Update requires the complete effective policy; changing severity alone does not
reapply defaults. Tightening policy affects new writes, not existing valid
dismissals (including permanent). Client sends only a scope; server computes
duration expiry from the stored policy and server UTC now. No preset/user-selected
seconds, clock or expiry. Duration ranges apply per notification, not user.

## Identity and dismissal semantics

`Identity.NotificationReaderIdentity` is an internal trusted service input, **not a
request/response DTO**. `INotificationReaderIdentityAccessor.GetCurrent()` returns null
only for actual anonymous access. For authenticated access it must obtain nonblank
issuer/subject and exact roles from the validated JWT, plus a nonblank, trusted
**application login** ID from the authentication integration. Invalid credentials
must not fall back to public success. Issuer/subject failure is
`notifications.invalid_identity`; absent trusted login ID is
`notifications.login_session_required` (403), including authenticated reads, to avoid
silently ignoring a previous session dismissal.

Login ID survives token refresh, reconnect and tab navigation but changes on a fresh
application login; tabs sharing a login share that server dismissal. Keycloak `sid`
alone is not yet approved (may identify a wider SSO session). API phase must establish
fresh per-login WebUI ticket/token integration and trustworthy service verification,
not take an unsigned caller header, body/query identity or cookie GUID on trust.
Forward the validated token through the BFF; how the trusted application login is
bound to it remains an API/auth prerequisite. Do not expose this ID in DTOs or push.

Separate keys:

* Account: `(issuer, subject, notificationId)`, one Duration OR Permanent state.
* Session: `(issuer, subject, notificationId, loginSessionId)`, Session state only.

PUT Duration/Permanent atomically replaces account choice only; PUT Session upserts
current-login state only. Neither clears the other layer or other logins.
Suppression is the OR of a permanent account state, account duration with
`serverNow < dismissedUntilUtc`, and current-session state. Response returns both
layers, so an account suppression is not concealed by a session choice.
Duration alone has a nonnull DismissedUntilUtc; other scopes have null.
Repeating an identical still-effective PUT is idempotent: preserve DismissedAtUtc and
expiry, not sliding snooze extension. Repeating duration after expiry starts a new
duration; selecting a different account scope replaces it. Serialize concurrent writes
per account key; last committed different account choice wins. Session writes in
concurrent logins cannot overwrite one another. Retries emit hints only if state changes.

DELETE uses required query `scope=account|currentSession|accountAndCurrentSession`
(no request body). It removes exactly the selected layers, idempotently. Never all
other login sessions. Missing state is success, but missing/inaccessible notice is
404. Return remaining state and `IsActive` evaluated at response server time; restore
is not a promise to show an ended, future or otherwise suppressed notice. Show again
can remove account/current-session state for future/ended notices while still
audience-authorized. List only effective account/current-session state, including
future/ended notices; omit expired durations and targets the reader no longer matches.
Session cleanup follows trusted login expiry/revocation (not a fixed snooze duration).
Logout ends that login's applicability; offline records may be garbage collected.

Anonymous dismissal is **UI-only per-tab sessionStorage**, public notices only, keyed
by app and UUID, surviving reload but not a fresh tab session. No backend record,
no claim that storage is cross-tab, no migration into account on login. Keep anonymous
storage separate; resume it on logout. Storage failure must be visible, not a false
saved dismissal. Anonymous tabs still need scheduled expiry/reconciliation.

## Endpoints to implement (typed Minimal API results / generated typed HTTP client)

Paths are `NotificationRoutes` constants. Validated authentication is required except
active's genuine-anonymous branch. All responses are `Cache-Control: no-store`;
public active must not inherit mandatory login. Mutations use existing OIDC/Keycloak
authorization and BFF same-origin/antiforgery protections, never caller-selected hub
groups/identity. Register `Notifications.Admin` using the existing
`AdministrationAuthorization` transformed permission claim `velotime_permission`,
requiring authenticated **velotime.admin**. Do not invent an Administrator role or
grant facilities/timing/statistics administrators notice-writing rights.

| Method/path | Input | Success |
|---|---|---|
| GET /api/notifications/active | pageSize, continuationToken query | 200 ActiveNotificationsDTO |
| PUT /api/notifications/{id}/dismissal | DismissNotificationRequest body | 200 NotificationDismissalDTO |
| DELETE /api/notifications/{id}/dismissal | required restore scope query | 200 RestoreNotificationDismissalDTO |
| GET /api/notifications/dismissals | pageSize, continuationToken query | 200 NotificationDismissalsPageDTO |
| GET /api/admin/notifications | pageSize, continuationToken query | 200 NotificationAdministrationPageDTO |
| GET /api/admin/notifications/{id} | id | 200 NotificationAdministrationDTO |
| POST /api/admin/notifications | CreateNotificationRequest | 201 NotificationAdministrationDTO + Location |
| PUT /api/admin/notifications/{id} | UpdateNotificationRequest | 200 NotificationAdministrationDTO |
| POST /api/admin/notifications/{id}/end | EndNotificationRequest | 200 NotificationAdministrationDTO |

Admin list includes ended/future notices. No physical delete. Update/end require
ExpectedVersion; stale version is 409. End sets effective end to server UTC now,
retaining an earlier end; repeated end of an already-ended notice is a no-op after
checking version. Ending a not-yet-started notice is allowed (cancellation); do not
move its start or activate it. Create/update otherwise require EndsAtUtc > StartsAtUtc
when present. All request DateTimeOffset offsets must be zero, all output UTC.
Active iff `starts <= now` and (`ends == null` or `now < ends`), audience matches,
and neither dismissal layer suppresses it. At exact snooze expiry it is eligible.
Dismiss PUT requires active schedule, audience match and allowed policy (existing
suppression does not block a retry/change of scope); inaccessible/non-active is 404.
Reads/restores/management share audience authorization. 404 masks existence for
nonmatching targets; admin policy denied is 403.

Errors are RFC ProblemDetails with stable extension `code` from NotificationErrorCodes:
400 malformed/invalid content, enum, audience, schedule, policy, duration, page/token
or restore scope; 401 invalid/missing authentication on protected endpoints; 403 invalid
validated identity/login session, forbidden dismissal scope or denied admin policy;
404 missing/inaccessible resource; 409 stale version or expired snapshot.
503 dependency failures remain failures; clients show localized retry/errors, never
empty successful snapshots or saved-state fallbacks.

### Bounded paging and deadlines (no silent truncation)

All lists accept pageSize 1..100 (default 50) and opaque continuationToken. Sort
strictly by UUID ascending, no offset paging. Null token starts a new snapshot;
nonnull response token means more pages, null means complete. Empty pages may only
finish a snapshot. UI must drain/queue **all pages**, not interpret page one as the
full set or prune unseen notices before completing a replacement snapshot.

Snapshot is evaluated at AsOfUtc. Continuations are integrity-protected and bind
operation, page size, sort position, as-of/expiry and relevant revision to the current
anonymous/authenticated issuer-subject-login and exact authorization/roles.
No readable identity data in tokens; encrypt or use opaque server handles.
Reject malformed/mismatched tokens (400), and changed revision, expired snapshot or
changed authorization (409 snapshot_expired). Reauthorize every page, including roles.
Storage/API owner may implement materialized opaque snapshots or revision-checked
keyset paging, but must not omit/duplicate matching rows under concurrent writes.
For revision-checked paging: notice mutations and relevant account/session writes
invalidate the snapshot; never merely skip inserted rows behind the cursor.
Administration and dismissal-management pages obey the same consistency rule.

ServerNowUtc is fresh on each response; AsOfUtc/expiry and active NextChangeAtUtc remain
constant across a snapshot. SnapshotExpiresAtUtc is no later than as-of + 300 seconds,
or the earliest relevant schedule/snooze deadline, whichever comes first. If a boundary
is reached while paging, return 409 and restart, not an inconsistent final page.
Clients preserve the previous complete snapshot and show visible bounded retry on
failure, but still remove expired/private content locally and on auth changes.

Active NextChangeAtUtc is the earliest future time the **entire authorized result**
could change: starts, ends and valid duration dismissals, including currently suppressed
and future notices outside this page. Null means no known deadline, not no paging.
It may conservatively wake earlier than necessary; never later than an eligibility
boundary. Do not derive it from page one/visible rows only. Private targets never
contribute to anonymous deadlines. Use server time to schedule refetch, not browser
clock assumptions. Subscribe before fetching; refetch on deadlines, reconnect,
auth changes and tab resume. Bounded snapshots recover missed hints.

## Refresh event / outbox / scheduled invalidation

`Messages.NotificationsRefreshRequestedV1(Guid EventId)` is the sole initial event.
Use `NotificationMessaging` constants: topic `velotime-notifications`, logical
subscription `webui-notifications`, `MessageName` header, filter
`MessageName = 'NotificationsRefreshRequestedV1'`, non-session subscription,
transport MessageId = EventId. Retries preserve that ID. No source-domain consumers.

**Static broadcast** is intentional: bridge sends the same minimal refresh hint to
all hub connections (including anonymous), not private groups. Hub method
`NotificationsRefresh`, payload is this same event. No content, notification UUID,
target, recipient, account/session identity, revision, reason or scheduled timestamp.
Only trusted producers publish. Active HTTP endpoint performs exact audience filtering.
Old AND new viewers therefore reconcile after an audience edit without requiring
prior-recipient routing fields or hashing private identity into broker headers.
Observers can see refresh timing, never which notice/user changed. Accept that
bounded timing side channel; coalesce broadcast invalidations to reduce traffic.
This is a freshness hint, not proof a specific item is visible or a dismissal succeeded.
Do not add future channel events/envelopes until a concrete consumer requires them.

Commit notice/dismissal mutations and durable outbox hint in the **same service DB
transaction**. No direct-send success fallback. At-least-once dispatch retries until
acknowledged; duplicate/out-of-order hints are harmless refetches, never duplicate
snackbars. UI deduplicates content by UUID/version, not just EventId. Restore emits
on real state change; no-op operations need not emit.

Durable worker wake-ups for starts/ends and duration expiry are in scope. Register,
reschedule/cancel with state mutations transactionally. At deadline, worker rechecks
authoritative current notice/dismissal state, atomically marks due work and enqueues
an outbox refresh. Stale jobs after edit/replacement/restore must not change state or
resurrect a notice. Catch up overdue work after restart; multiple replicas must claim
work safely. Scheduler is internal persistence, not a new public future-event API.
Snooze expiry works via client deadlines even without push; durable snooze wake-ups
also refresh idle connected clients. No row need be deleted to become eligible.

Platform owns topology provision; producers/consumers disable runtime provisioning.
WebUI bridge uses one logical competing subscription **plus a SignalR Redis
backplane** so all replicas' connections receive hints. One competing consumer
without backplane is insufficient. Same-origin cookie-authenticated hub; no browser
URL access tokens; account/private content only via authorized snapshots. One
connection per active UI across InteractiveAuto hydration; reconnect invalidates
local snapshots, logout clears private notices and authenticated identity state.

## Handoff / acceptance evidence required

* Data & Migrations: exclusive schema, composite separate dismissal keys, atomic
  account replacement/idempotency, notice versions, paging revision, transactional
  outbox and durable deadline claims/rechecks. No schema edited here.
* API owner: core manual create/read/update/end/dismiss/restore services, validation,
  identity adapter, route handlers/mappers, NSwag client and OpenAPI, policy registration.
* API + WebUI + Platform: settle trusted fresh application-login binding before
  authenticated notifications go live. Missing session identity explicitly fails.
* Messaging & Workers: implement this exact v1 event, outbox dispatch and schedule
  invalidation. Platform: topology, auth, processor/API/migration deployments and Redis.
* WebUI: same-origin hub/bridge/BFF, both render modes, snackbar queue/reconciliation,
  anonymous tab storage, permitted/default actions, Show again and admin editor.
* Tester (next gate), then read-only Reviewer: enum JSON roundtrip and invalid numeric/
  missing values; defaults/complete overrides (info + duration explicit), cap 0/1/
  31536000/31536001; exact audience matrix including same subject/different issuers,
  role casing, anonymous isolation and no private DTO/hint fields; UTC zero offset and
  starts/end/snooze equalities; two concurrent logins, account replacement/retry,
  partial/all-current restore and remaining suppression; >100 notices, concurrent
  paging mutation, expired token and deadline outside page one; outbox rollback/
  retries, duplicate/out-of-order hints, stale schedule jobs/restart; two WebUI
  replicas/backplane, login/refresh/reconnect/logout, storage failures and visible
  API failures. No infrastructure-backed behavior is claimed in this contract phase.

Before integrated push acceptance, agree a measurable delivery latency target with
Coordinator/Platform/Tester; "eventually received" alone is not instant delivery.

### Contract-phase checks

* `dotnet build src/Module.Notifications.Interface/VeloTime.Module.Notifications.Interface.csproj --configuration Release`:
  passed, 0 warnings / 0 errors.
* `dotnet build VeloTimer.sln --configuration Release`: passed, 128 warnings in
  existing projects / 0 errors (including dependency vulnerability warnings; not
  changed as part of this contract-only scope).
* Isolated temporary console checker: 59 passing assertions covering all enum
  wire values, numeric/unknown rejection, DTO privacy and scope-only shapes, defaults/
  bounds constants, broker filter, omitted policy and snapshot/dismissal/event JSON
  roundtrips. Not a repository test suite or a domain/infrastructure test.
* `dotnet sln VeloTimer.sln list` confirms interface registration; `git diff --check`
  passed. Tester owns durable regression tests and broader checks at the next gate.
