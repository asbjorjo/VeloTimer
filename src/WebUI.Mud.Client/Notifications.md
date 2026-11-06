# Notifications UI / Tester handoff

## Preserved presentation

MainLayout shows one snackbar, queued by severity then UUID. Complete snapshots
reconcile UUID/version, changed/deleted/ended content and locally measured
deadlines. Concurrent host loads are coalesced; requests and timers are cancelled
on disposal/auth transitions. Partial pages never replace complete state.
Revision-conflict retries remain bounded to three.

Titles/bodies remain encoded, localized en/nb-NO plain text. Timestamps and units
use existing user display preferences. Actions are keyboard buttons with polite
live regions and wrapping content. Explicit dismissals alone hide notices;
failed saves retain the notice and localized retry feedback.

Anonymous dismissals remain tab-local `sessionStorage` at
`velotime:notifications:anonymous:v1:<UUID>`, ignored while authenticated and never
migrated into account state. Storage failures remain visible.
Trusted request-local BFF capabilities govern session actions; missing `sid`
does not hide permitted account actions or automatically save a fallback scope.
Admin/profile authorization, optimistic editing, UTC editing, and layer-specific
restores are unchanged. Backend authorization remains authoritative.

## Replacement transport

The shared UI contract was compiled before implementing both render modes.
See `../WebUI.Mud/Notifications/README.md` for exact implementation paths,
DI registrations, Redis channels, retries, and authentication limits.

WASM uses Microsoft's .NET `HubConnectionBuilder`; InteractiveServer receives
server-local subscriptions fanned out across replicas through Redis. JavaScript
is BFF fetch/abort and anonymous storage only. No protocol parser, custom
networking, cross-tab logout channel, logout form listener, or anonymous probe
remains. Authentication changes and 401/403 clear private snapshots when
observed; **instant stale-DOM clearing in inactive tabs after remote Keycloak
logout is not claimed**.

## Verification

Commands run from `/workspaces/VeloTimer-notifications`:

* Targeted Platform tests: **72 passed, 0 failed, 0 skipped**, including three
  real-Redis fixtures. Latest measured bridge-to-both-replicas/Microsoft-client
  latency: **19 ms** on disposable Redis 7.4.12.
* Node browser helpers: **6 passed, 0 failed, 0 skipped**.
* Release builds: host **0 errors / 4 advisory warnings**; client
  **0 errors / 1 advisory warning**.
* `git diff --check`: passed.

```sh
OIDC_AUDIENCE=https://build.invalid \
VELOTIME_TEST_REDIS='127.0.0.1:16379,abortConnect=false' \
dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj \
  --configuration Release --no-restore \
  --filter 'FullyQualifiedName~Notifications|FullyQualifiedName~WebUiBffTests.Oidc|FullyQualifiedName~OidcLocalizationTests|FullyQualifiedName~MainLayoutCacheIdentityTests' \
  --logger trx -v quiet

node --test src/VeloTime.Platform.Test/NotificationsBrowser.test.mjs

dotnet build src/WebUI.Mud/VeloTime.WebUI.Mud.csproj \
  --configuration Release --no-restore -v quiet
dotnet build src/WebUI.Mud.Client/VeloTime.WebUI.Mud.Client.csproj \
  --configuration Release --no-restore -v quiet
git diff --check
```

The targeted suite covers official-client startup/reconnect/failure/cancellation/
restart/disposal, callback deduplication/draining, component first-render gating,
circuit dispatch, auth restart, 401/403 clearing, and existing BFF/OIDC/paging/
localized admin/profile behavior.

Opt-in Redis tests cover two-replica WASM/server delivery, no double hub broadcast,
EventId redelivery deduplication, unsubscribe/suspend/cancel/failure isolation,
invalid payload rejection, and actual Redis subscription-connection loss/recovery.
They invoke the actual bridge directly, **not a live Azure Service Bus broker**.
The local bridge-to-both-replicas-and-client target is <=2 seconds.

Node tests cover same-origin BFF constraints, CSRF, capability headers, storage
failure/persistence, and request cancellation. Removed fake WebSocket/logout
tests were replaced with the relevant browser-helper and real .NET lifecycle
fixtures, not a claim of browser-authenticated logout delivery.

No browser tooling was available; no visual/responsive or full browser validation
is claimed. Known NuGet advisories include the centrally pinned new client's
transitive `Microsoft.AspNetCore.SignalR.Common` 10.0.12 (NU1903), existing
WebAssembly.Server/MessagePack and Platform dependencies. Central upgrades remain
Coordinator/Platform-owned.

## Next owner: Coordinator selects Tester, then review/push PR 124

1. Exercise both InteractiveAuto paths: fresh prerender/hydration, navigation,
   anonymous reload/tab dismissal, and dispose/recreate. WASM should have one
   notification hub connection; Server should have none beyond the normal circuit.
2. Verify offline/startup exhaustion/reconnect, missed-hint snapshot repair,
   deadline/end removal and 401/403/503 error states; test active notification bursts.
3. Run authenticated account/session/no-sid controls, admin edit/end conflicts,
   profile restore layers, locale/timezone and keyboard/mobile wrapping.
4. Exercise application logout and remote Keycloak logout with multiple active/
   inactive tabs. Record actual cookie/token/circuit behavior and timing; do not
   equate stale-DOM hiding with session revocation or promise immediate cross-tab
   clearing. Any identity-infrastructure gap belongs to Coordinator/Platform.
5. Confirm deployment Redis ACLs for
   `velotime:webui:notifications:server-refresh:v1`, ingress affinity, shared cache
   configuration and existing broker topology. Measure live outbox/broker/bridge/
   browser timestamps separately; local fixtures do not establish that latency.
