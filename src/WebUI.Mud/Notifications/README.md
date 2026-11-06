# Notifications WebUI BFF and delivery

WebUI calls the committed `INotificationsClient`, never a service database. All nine
routes are mirrored at their shared paths. Active is genuinely anonymous; rejected
cookies or browser bearer credentials fail, never fall back to public data. Reader
mutations require cookie authentication and antiforgery; authoring additionally
requires `Notifications.Admin` / `velotime_permission=velotime.admin`. Safe typed
Problems preserve upstream status/code without exposing upstream details.

The existing Duende user-access-token handler forwards the Keycloak bearer token.
There is no Notifications issuer, signing key, assertion, application login ID,
or second token-validation layer. API authorization uses the validated access
token's issuer/subject/roles and optional `sid`. Missing `sid` does not block
authenticated active reads, account dismissals/restores, or permitted admin operations.
Session mutations and restores involving currentSession remain API-authorized and
fail with 403 `notifications.login_session_required` when the access-token `sid` is absent.

The UI-only `/api/notifications/session` cookie-status probe returns exactly
`{ "authenticated": bool, "hasSession": bool }`, no-store, without calling the
Notifications API. No actual `sid` is exposed. `hasSession` uses the protected
cookie principal's nonblank OIDC `sid`; it never uses browser headers, query values,
or an unvalidated JWT payload. Reader response headers `X-VeloTime-Authenticated`
and `X-VeloTime-HasSession` carry the same boolean capabilities for presentation.
The host applies them only with a completed snapshot under its existing epoch gates.
A second tab suspends content requests after logout until this probe confirms
genuine anonymous state; it never forwards another authenticated snapshot while
the logout POST is still in flight.

## Exact configuration / Platform integration

Existing OIDC, shared WebUI cookie/Data Protection/token storage and API discovery
still apply. No Notifications signing configuration is required. Snapshot
Data Protection is independent and remains unchanged. Deployment must provide TLS,
synchronized clocks and Notifications API discovery.

Platform must ensure Keycloak emits matching `sid` in the validated ID token and
access token, including refreshed access tokens. The trusted ID-token claim is
only a UI capability hint: if it exists but the access token lacks `sid`, the API
still rejects session mutations without changing state. If absent, the UI hides
session actions, retaining all permitted account actions (no automatic fallback
dismissal). The same Keycloak SSO `sid` shares session dismissal across tabs/refresh;
different `sid` values remain isolated.

`ConnectionStrings:MessageBus` supplies SlimMessageBus Azure Service Bus.
`ConnectionStrings:cache` supplies the existing Redis connection (not a new
`Redis` connection name). The SignalR backplane has channel prefix
`velotime:webui:notifications`. Runtime topology provisioning is disabled.
Provision `velotime-notifications` / non-session `webui-notifications`, filter
`MessageName = 'NotificationsRefreshRequestedV1'`, through the Platform job.
The competing-consumer bridge broadcasts only `{eventId}` on
`NotificationsRefresh`; Redis fans it out to every WebUI replica. Anonymous hub
connections are intentional and cannot select groups or invoke content methods.
Hub connections close at cookie authentication expiry.

Project-local references are UI-owned; central versions are not. Platform must
provide `Microsoft.AspNetCore.SignalR.StackExchangeRedis` **10.0.12** in
Directory.Packages.props (its separate SignalR.Client version is not needed by
this first-party browser transport). Existing centrally-versioned SlimMessageBus
AzureServiceBus and SystemTextJson packages are referenced by WebUI.

## Both InteractiveAuto modes

The first-party `notifications.js` module runs in the browser for both server
circuits and WASM. It uses same-origin cookies and CSRF fetch, not access-token
URLs or long-lived server HttpContext. It negotiates SignalR JSON/WebSockets,
handshakes before snapshot loading, pings, detects transport silence, reconnects
with capped backoff, deduplicates minimal EventIds and repairs missed hints on
tab resume/periodic checks. WebSocket support and usual SignalR negotiation
affinity are required at the ingress; Redis does not replace sticky negotiation.
There is no CDN/vendor JS or second .NET/browser connection.

All list adapters drain revision-bound pages before publishing replacements.
Only explicit `notifications.snapshot_expired` 409 restarts; three attempts
maximum. Other errors and repeated tokens fail without returning partial lists.

## Validation baseline

On the assigned `3e42f08f` tree after UI simplification:

* `dotnet build src/WebUI.Mud/VeloTime.WebUI.Mud.csproj --configuration Release --no-restore -v quiet`:
  passed, 0 errors, 3 existing NuGet advisory warnings on the final incremental build.
* `dotnet build src/WebUI.Mud.Client/VeloTime.WebUI.Mud.Client.csproj --configuration Release --no-restore -v quiet`:
  passed, 0 errors/warnings on the final incremental build.
* `OIDC_AUDIENCE=https://build.invalid dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj
  --configuration Release --no-restore --filter 'FullyQualifiedName~Notifications|FullyQualifiedName~WebUiBffTests.Oidc|FullyQualifiedName~OidcLocalizationTests'
  --logger trx -v quiet`: **40 passed, 0 failed, 1 skipped** (Redis opt-in).
* `node --test src/VeloTime.Platform.Test/NotificationsBrowser.test.mjs`:
  **6 passed, 0 failed**.
* `git diff --check`: passed.

Checks include real in-process OIDC callback/ticket reload with Duende bearer
forwarding to a stub API, bool-only capability/no browser-selected sid, localized
rendering and no-sid account/session controls, paging and logout suspension.
These do not validate live Keycloak access-token mapper alignment, token-refresh
sid stability, Azure Service Bus, Redis delivery or full-browser interaction.
No browser tools were available; no visual validation is claimed.
No central packages, AppHost, charts, API tests or snapshot Data Protection changed.

## Integrated Tester checks

`src/VeloTime.Platform.Test` now contains focused notification BFF/capability,
browser transport and rendering regressions, and
`src/VeloTime.Platform.Test/NotificationsBrowser.test.mjs` verifies browser-side
storage and SignalR transport behaviors with local fakes. Run the .NET project and
Node test directly before claiming UI regression coverage. These are unit/component
checks; they do not prove live two-replica Redis backplane delivery or authenticated
browser behavior with Azure Service Bus.

An opt-in `NotificationsRedisBackplaneTests` starts two TestServer WebUI hub hosts,
uses the real Redis SignalR backplane and WebSocket JSON handshake, invokes the
actual broker bridge against a fixture event, and asserts client receipt within
2 seconds. It bypasses Azure Service Bus and a full browser. Run it against a
disposable Redis server:

```sh
VELOTIME_TEST_REDIS='127.0.0.1:6379,abortConnect=false' \
  dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj \
  --configuration Release --filter FullyQualifiedName~NotificationsRedisBackplaneTests
```

The measurable local instant-delivery acceptance target is **<= 2 seconds** from
outbox event publication eligibility through SignalR bridge receipt to the connected
browser refresh callback. Neither unit tests nor static chart rendering establish
this target; live infrastructure validation must report actual timestamps and
latency. The disposable local two-host fixture measured 42 ms from bridge invocation
to TestServer WebSocket receipt; it does not include outbox polling, Azure Service Bus,
or a real browser.
