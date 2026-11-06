# Notifications delivery and authentication

## Exact render paths

`WebUI.Mud.Client/Services/INotificationRefreshSource.cs` is a UI-only contract.
It returns an inactive, async-disposable subscription, with refresh, connected
(including reconnect), and transport-failure callbacks. `NotificationsHost`
subscribes only on its first interactive render and marshals callbacks through
the component's `InvokeAsync`. Nothing subscribes during prerender.

* **InteractiveWebAssembly:** `WebUI.Mud.Client/Program.cs` registers
  `WasmNotificationRefreshSource`. It builds the official
  `Microsoft.AspNetCore.SignalR.Client` `HubConnection` to the same-origin public
  `/hubs/notifications` endpoint. Browser cookies are automatic; there is no
  access-token provider, bearer URL, identity group, or custom protocol parser.
  The host requests its first BFF snapshot as soon as subscription activation
  returns, independently of initial Hub negotiation; connected/reconnected
  callbacks refresh again to repair any missed hints.
  Initial startup has four attempts (immediate, then 1/2/4-second delays).
  Microsoft's automatic reconnect has four delays (0/2/10/30 seconds).
  Connected/reconnected callbacks refresh authoritative content. Exhausted
  retries show a warning while ordinary snapshot deadlines/polling continue.
  Observed auth changes stop/restart the connection; disposal cancels startup,
  removes handlers, and disposes the connection.
* **InteractiveServer:** `NotificationHostingExtensions.AddWebUiNotifications`
  registers scoped `ServerNotificationRefreshSource` and singleton hosted
  `NotificationRefreshFanout`. Circuit subscriptions receive server-local callbacks
  directly. There is **no loopback HubConnection and no notification browser
  WebSocket**. The application's normal Blazor circuit transport is unchanged.
  Circuit auth comes from `AuthenticationStateProvider`, not captured HttpContext.

Both modes retain `NotificationsBrowserService` for browser-origin BFF requests.
`notifications.js` now exports only BFF request/abort and anonymous
`sessionStorage` helpers. It contains no SignalR networking, negotiation, JSON
framing, WebSocket, ping, reconnect, visibility listeners, BroadcastChannel,
logout form interception, or cookie-status probes.

## Cross-replica delivery

The existing Azure Service Bus topic `velotime-notifications`, competing logical
subscription `webui-notifications`, and filter
`MessageName = 'NotificationsRefreshRequestedV1'` remain unchanged.
Runtime topology provisioning remains disabled.

`NotificationsRefreshBridge` broadcasts `{eventId}` once through
`IHubContext<NotificationsHub>.Clients.All` and the existing SignalR Redis
backplane (prefix `velotime:webui:notifications`). It also publishes **only the
EventId UUID** to the dedicated literal Redis channel
`velotime:webui:notifications:server-refresh:v1`.

Every replica subscribes to that second channel once and dispatches its own
circuit subscribers. The Redis callback **never broadcasts to SignalR**, avoiding
double fanout. Each UI subscription bounds EventId deduplication to 256 IDs and
uses a single bounded callback pump: while a callback is in flight, refresh
hints coalesce to at most one follow-up (no more than two refresh callbacks per
pump). A sustained burst cannot extend that pump indefinitely; later hints can
start another bounded pump. Connection state keeps at most one pending callback
for each distinct `Connected` and `TransportFailed` kind, coalescing repeats
without losing both kinds during a refresh burst. Redis delivery does not await
or create one waiting callback task per hint; slow/failing circuit callbacks do
not hold Redis's serial message handler or fail another subscriber.

Failure of either bridge operation propagates for broker retry; repeated hints
are harmless. Redis pub/sub is not durable: initial/reconnected snapshots and
deadline polling repair hints lost during disconnection.

The fanout reuses the existing DI-owned `IConnectionMultiplexer` from the `cache`
registration. Initial Redis subscription failures (Redis errors and timeouts)
are logged and retried in the background with exponential delays from 1 to 30
seconds; they do not block host startup. Active circuits report transport failure
until the channel is subscribed, then refresh their snapshots on connection.
After a queue is established, the Redis client restores it on reconnect. Shutdown
cancels retries, removes connection-event handlers and unsubscribes the channel,
including a subscription that completes late, but never disposes that shared
connection. Other services' own Redis startup requirements are unchanged. No
per-replica broker subscriptions or server hub clients are created.

Deployment still needs `ConnectionStrings:cache`, `ConnectionStrings:MessageBus`,
Notifications API discovery, shared cookie/Data Protection/token storage, TLS,
and ordinary SignalR ingress affinity. Redis ACLs must allow publish/subscribe
on the new server-refresh channel as well as existing backplane channels.
No AppHost/chart/topology/package-version changes were made. Client's new
PackageReference uses the existing central **10.0.12** version.

## Authentication semantics and limits

The BFF continues to use the committed typed Notifications client and existing
Duende Keycloak token forwarding. Rejected/malformed cookies or browser bearer
credentials fail rather than downgrade to anonymous data. Reader mutations
require cookie auth/antiforgery; admin operations also require the existing global
permission. The API remains authoritative, using validated Keycloak claims.
No new issuer, signing secret, login-session identity, or database access exists.

Request-local boolean capability headers are applied only with a complete,
epoch-validated snapshot. Nonblank trusted `sid` enables session actions; account
actions remain available without it. Missing access-token `sid` is still rejected
by the API for session mutations. Keycloak ID/access-token mapper alignment,
including refreshed tokens, remains an integration requirement.

Private content clears when the host observes an authentication-state change or
an authoritative 401/403. An anonymous completed snapshot restores tab-local
anonymous dismissal filtering. After an observed provider logout, still-authenticated
snapshots are discarded until normal snapshots observe anonymous state (or the
provider observes login); no probe or cross-tab protocol is used.
Existing application OIDC/cookie logout and token
revocation are unchanged. The unused, existing boolean `/api/notifications/session`
capability endpoint is retained for compatibility; notifications no longer probe it.

**There is no notification-specific cross-tab logout coordination.** UI hiding
does not revoke an identity session. No explicit Keycloak backchannel-logout
handler or periodically revalidating circuit provider was found in the inspected
application registration. Keycloak-only logout cannot be claimed to immediately
clear inactive-tab DOM, invalidate an existing local cookie, or revoke an already
issued JWT. Those depend on existing identity policy, expiration, and application
logout handling. Snapshot polling normally runs at up to 60 seconds, subject to
browser throttling; it is not an instant cross-tab logout guarantee.

## Validation / next owner

Contract-first client Release build passed before implementations (0 errors,
3 pre-existing BL0005 warnings). Behavioral fixtures use the official .NET client,
not handcrafted SignalR frames. Redis fixtures run two TestServer replicas and
real disposable Redis, bypassing Azure Service Bus and a full browser.

Current exact checks and interaction handoff are in
`../../WebUI.Mud.Client/Notifications.md`.
