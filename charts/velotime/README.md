# Notifications deployment prerequisites

The Notifications API and processor are included in the chart by default. The
processor image is `ghcr.io/asbjorjo/velotime-module-notifications-processor`
with the chart's `global.image.tag`; the API has its own matching image. The
Notifications migration hook runs before the API/processor workloads.

The Notifications API is internal only in this release. WebUI calls it through
Kubernetes service discovery; browser notification requests must continue to
reach the WebUI BFF on the WebUI hostname. The API Service remains `ClusterIP`,
and this chart does not create an API HTTPRoute. The API routes do not implement
the WebUI cookie session probe. WebUI's `/api/notifications/session` reports
boolean capability from its trusted cookie claims; API calls use the existing
Keycloak bearer token. Legacy `module-notifications-api.httpRoute.*` overrides
are unsupported and cannot publish the API. Do not redirect or proxy browser
`/api/notifications/*` or `/api/notifications/session` traffic away from WebUI.

The checked-in realm import is used only by local Development and has no explicit
session-ID mapper. Production uses an externally managed Keycloak whose server
version is not pinned by this chart. Before enabling session dismissal, the
Keycloak administrator must verify that the deployed version issues the same
nonblank `sid` in signed ID and access tokens on both login and refresh. Configure
only a session-ID mechanism supported by that exact version if required; do not
assume a mapper/provider default or name without version-specific evidence.
Without an ID-token `sid`, WebUI keeps session actions unavailable; without an
access-token `sid`, the API rejects session-specific operations while account
operations remain available. Existing validated Keycloak token claims remain the
only source of session identity; no replacement identity provider is required.

Use the chart's existing Secret mechanisms (prefer an external-secrets controller
in production):

* If `module-notifications-api.existingSecret` is set, it must include
  `ConnectionStrings__cache` and `ConnectionStrings__velotimedb`.
* Runtime `ConnectionStrings__MessageBus` values are required by WebUI and the
  Notifications processor. Their `existingSecret` must also include the
  corresponding `ConnectionStrings__cache`; the processor additionally needs
  `ConnectionStrings__velotimedb`. If chart-managed service secrets are used
  instead, set the existing `secrets.MessageBus` values without committing
  credentials.
* The default `messageBusTopology` hook provisions the non-session
  `webui-notifications` subscription and its
  `MessageName = 'NotificationsRefreshRequestedV1'` filter. Its management
  connection is separate from runtime `MessageBus` and must have Manage rights.
  Keep the Valkey PVC enabled and retained: API snapshot Data Protection keys are
  stored in Redis under the stable `VeloTime.Notifications` application name.

Use TLS, synchronized clocks and a shared Redis backplane for multi-replica WebUI
SignalR delivery. Helm rendering and tests do not validate live Keycloak token
claims, Service Bus, PostgreSQL, ingress affinity, or authenticated end-to-end
browser push delivery. The opt-in `NotificationsRedisBackplaneTests` does exercise the
actual SignalR Redis backplane between two local WebUI hub hosts; see the WebUI
Notifications README for its `VELOTIME_TEST_REDIS` command.

The local instant-delivery acceptance target is **<= 2 seconds** from an outbox
refresh event becoming publishable until the browser refresh callback runs on every
connected WebUI replica. Measure bridge receipt and client callback timestamps. A
chart render, topology unit test, or Redis health check alone does not demonstrate
that delivery target; record live broker/backplane/browser results separately.
