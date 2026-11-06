# Notifications WebUI BFF and delivery

WebUI calls the committed `INotificationsClient`, never a service database. All nine
routes are mirrored at their shared paths. Active is genuinely anonymous; rejected
cookies or browser bearer credentials fail, never fall back to public data. Reader
mutations require cookie authentication and antiforgery; authoring additionally
requires `Notifications.Admin` / `velotime_permission=velotime.admin`. Safe typed
Problems preserve upstream status/code without exposing upstream details.

`OidcEvents.TokenValidated` creates a fresh protected-ticket application login ID;
renewal/refresh preserve AuthenticationProperties. It is not a claim and is not
serialized into WebAssembly auth state. The outbound assertion handler runs after
Duende's access-token handler, validates the actual JWT against OIDC signing keys,
issuer, `OIDC_AUDIENCE`, lifetime and the protected ticket's exact issuer/subject,
then uses the shared RSA-PSS signer. The assertion is capped by ticket expiry.
Incoming assertion headers are stripped. No login ID/assertion/key reaches browser
DTOs, JS, URLs, logging or hub messages.
The UI-only `/api/notifications/session` cookie-status probe returns just a boolean,
no-store, without calling the Notifications API or issuing an assertion. A
second tab suspends content requests after logout until this probe confirms
genuine anonymous state; it never forwards another authenticated snapshot while
the logout POST is still in flight.

## Exact configuration / Platform integration

`Notifications:SessionAssertion`:

* `Issuer`, `Audience`: exactly match the API's configured signer/audience.
* `KeyId`: installed public-key ID on every API replica.
* `PrivateKeyPem`: secret-store injection only; RSA private key >=2048 bits.
* `LifetimeSeconds`: default **120**, range 1..300 and <= API maximum.

Invalid/missing signing configuration fails startup. Existing OIDC, shared WebUI
cookie/Data Protection/token storage and API discovery still apply. Deployment
must provide TLS, synchronized clocks and Notifications API discovery. Key
rotation/revocation semantics are in the validated Interface Identity README.

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

Native host restore on base `874b3a0c` fails **NU1010** for the missing central
SignalR backplane PackageVersion; no central file was edited. A provisional
Release host build and 21 notification .NET tests succeeded using the Platform
owner's central manifest read-only via:

`-p:DirectoryPackagesPropsPath=/workspaces/VeloTimer-notifications-platform/Directory.Packages.props`

The five `node --test src/VeloTime.Platform.Test/NotificationsBrowser.test.mjs`
tests also passed. This is compilation/transport evidence, not integrated
OIDC/broker/Redis/browser delivery validation. Repeat ordinary builds and tests
after Platform integration. Existing NuGet advisory warnings remain; package
remediation is central-owner work.
