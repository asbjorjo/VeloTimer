# Notifications presentation and Tester handoff

## Implemented UI

MainLayout hosts one notification snackbar at a time alongside the existing
MudSnackbarProvider. It queues by severity then UUID, deduplicates by UUID/version,
updates edited content, removes ended/deleted-from-snapshot content, and clears
private notices immediately on logout/auth failure. There is no automatic close,
save-on-timeout, or uncontrolled close icon. Only an explicit permitted action
can dismiss. Failed server/storage saves retain the notice and show a retryable
localized error. Bodies/titles are encoded plain text, selected in en/nb-NO.
Actions are keyboard buttons with polite live regions, and the content wraps
within the viewport. Durations use browser number culture; timestamps/editors
use the existing effective display preferences, not the server timezone.

All authenticated scopes allowed by the notice are available, with the configured
default prominent: information permanently, warning one day, error one hour.
Authors can override those policies and prohibit permanent dismissal. Anonymous
public notices always offer tab dismissal, regardless of authenticated policy.
Anonymous state is exclusively `sessionStorage` under
`velotime:notifications:anonymous:v1:<UUID>`. It survives reload of the same tab;
it is not an account record or cross-tab shared preference. On login it is ignored,
not migrated; on logout it resumes. Storage denial is visible and does not falsely
hide a notice. Native browser duplication/opener storage-copy behavior is not
represented as server or cross-tab synchronization.

Host starts only after interactivity; no prerender connection. Subscribe precedes
snapshot. Hints/reconnect, deadline timers, tab visibility, auth changes and bounded
polling reconcile authoritative snapshots. A monotonically measured elapsed time
anchors deadlines to server-now. Failed snapshots retain only the previous complete
set; local end-time removal still runs. Paging revision restarts are bounded to
three. Concurrent hints are coalesced without concurrent host loads. Disposal
cancels timers/requests/subscriptions. A connection-owner handle prevents the old
hydration instance from disposing its successor (not an authentication identity).
Same-origin logout broadcasts contain only `"logout"`, never an identity or notice.
Logout cancels and suspends content requests. A second tab resumes anonymous
storage only after the UI-only no-store `/api/notifications/session` boolean probe
confirms the cookie has actually ended; the probe never calls the Notifications
API or issues an assertion.

`/admin/notifications` and navigation use the existing global permission policy.
List/create/edit/end cover everyone, authenticated, exact role, exact issuer+subject.
The editor uses existing ZonedInstantDraft/ZonedInstantEditor, preserves untouched
UTC precision/offsets, validates end > start and complete policy, and constrains
duration to 1..31536000 SI seconds. New severity changes select defaults; editing
severity retains the saved effective policy. Edit/end send optimistic ExpectedVersion.
409 retains the draft; explicit reload discards it. End uses a confirmation and
supports cancellation of future notices. Existing unsaved-editor tracking defers
display preference changes while editing. API authorization remains authoritative.

Profile Show again drains dismissal pages, displays effective account/current-login
layers, and offers account, currentSession, or both only when applicable. It uses
the returned remaining layers/IsActive, never promises an ended/future/suppressed
notice will appear, and never clears other application logins. Authentication
changes clear management content and invalidate in-flight results. Admin/profile
lists share host refresh signals without additional hub connections.

## Exact checks

* Existing typed Notifications client Release build: success, 0 warnings/errors
  (restore API first for build-generated client prerequisites).
* Client Release build: success; only three existing BL0005 warnings in
  TransponderManagement on its initial full compilation.
* Native host restore on base 874b3a0c: **NU1010** missing central
  Microsoft.AspNetCore.SignalR.StackExchangeRedis PackageVersion. No central edits.
* Provisional host Release build with Platform's committed b9031635 central
  manifest supplied read-only by `DirectoryPackagesPropsPath`: success.
* `OIDC_AUDIENCE=https://build.invalid dotnet test
  src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --configuration Release
  --filter FullyQualifiedName~Notifications --logger trx
  -p:DirectoryPackagesPropsPath=/workspaces/VeloTimer-notifications-platform/Directory.Packages.props`:
  30 passed, 0 failed/skipped. Tests cover BFF auth/CSRF/global permission/problems,
  actual-token assertion binding/ticket expiry/fresh login, all-page drain/restarts,
  localized escaped rendering/default choices, save failure/retry, UUID/version
  reconciliation/end/logout, author defaults/overrides/UTC and profile layer restore.
* `node --test src/VeloTime.Platform.Test/NotificationsBrowser.test.mjs`:
  5 passed, 0 failed. Native transport mocked at fetch/WebSocket/DOM boundaries.
* Both resource files contain the same 69 Notifications keys.
* Expanded notification + existing OIDC localization/resource/layout regressions:
  68 passed, 0 failed/skipped with the same test command and filter
  `FullyQualifiedName~Notifications|FullyQualifiedName~OidcLocalizationTests|FullyQualifiedName~LocalizationContractTests|FullyQualifiedName~MainLayoutCacheIdentityTests`.
  The layout fixture now supplies the new UI dependencies and explicitly verifies
  prerender does not request notification snapshots; original identity-race
  assertions remain unchanged. Final client/host builds had 3/6 warnings
  respectively, 0 errors.

No browser executable/tool was available. HtmlRenderer and mocked browser tests
are **not** visual, full browser interaction, real OIDC, broker or Redis validation.
Known central NuGet advisory warnings remain (MessagePack, WebAssembly.Server,
plus existing Platform test dependencies); no package versions were changed.
Generated API newline-only output is discarded, not committed.

## Integration / next owner

Coordinator integrates Platform b9031635 and the UI commits, then selects Tester.
Ordinary builds/tests must be repeated on that combined tree; the read-only central
manifest build is not a claim of integrated deployment acceptance. Runtime
configuration and topology are documented in `../WebUI.Mud/Notifications/README.md`.
Platform must configure cache/MessageBus/API discovery, matching signing/public keys,
protected key rings, ingress WebSockets/negotiation affinity and the Redis backplane.

Tester scenarios:

1. Both InteractiveAuto paths: fresh anonymous load, hydration, navigation, reconnect,
   single browser connection, cancellation/disposal; online/offline and API 401/403/503.
2. >100 notices/pages with writes between pages and expiring tokens: only full
   snapshots replace content; three explicit revision retries maximum.
3. All audience modes, exact role/issuer/subject and global-admin authorization;
   inspect public HTTP/push/browser state for sensitive data or assertion/login IDs.
4. Fresh OIDC application login vs shared SSO sid, refresh/renewal, concurrent logins,
   ticket expiry, logout during snapshot/save, same-origin second-tab logout.
5. Anonymous sessionStorage reload/new independent tab, storage denied, login does
   not inherit anonymous dismissals, logout resumes that tab's stored UUIDs.
6. Warning day/error hour/info permanent; overrides/forbidden permanent, all other
   allowed actions, failed saves/retry, no saved dismissal from elapsed snackbar time.
7. Profile account/current-login/both restore; remaining suppression, expired snooze,
   ended/future notices and no other login cleared.
8. Author both languages/all targets, duration bounds, UTC/DST gaps/repeats,
   optimistic edit/end conflict and explicit draft discard; material new UUIDs.
9. Activation/end/snooze deadlines during broker outage and visibility resume.
   Duplicate/reordered hints, two WebUI replicas through Redis, broker redelivery.
10. Keyboard focus/actions and screen reader announcements, mobile wrapping, en/nb-NO
    content/errors with opposing browser number/date preferences.

Record end-to-end committed-write → visible-update latency under two replicas.
No measurable latency acceptance target has yet been agreed; do not infer one
from unit tests or claim eventual delivery satisfies instant-push acceptance.
