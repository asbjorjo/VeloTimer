# Notifications platform and deployment

## Keycloak session capability

The Notifications API uses the existing Keycloak bearer-token validation and
accepts the token's optional `sid`; WebUI's `/api/notifications/session` reports
only booleans derived from the trusted cookie ID-token claims. There is no
separate Notifications identity; session identity comes only from existing
validated Keycloak token claims.

The checked-in realm import is
`config/keycloak/realms/velotime-realm.json`; AppHost imports it only for local
Development. It defines the VeloTime API audience/role mappers but has no
explicit session-ID mapper. Production Helm configuration points to an external
Keycloak and does not pin or install its server version. Neither the checked-in
realm nor the deployment config proves that every supported/deployed Keycloak
emits a `sid` claim in both token types.

Before enabling session dismissal operationally, the Keycloak administrator must
verify the actual deployed release and configure only a session-ID mechanism
supported by that release, if needed. Validate a fresh login and a token refresh:
the signed ID token and signed access token must each contain the same nonblank
`sid`. Do not assume a mapper/provider name or default behavior without evidence
for that server version, and do not install a custom plugin or add another
identity provider for this feature. If the ID token lacks `sid`, WebUI keeps
session actions unavailable; if the validated access token lacks it, the API
rejects session-specific operations while account-level operations remain
available. Treat a missing or mismatched claim as session capability being
unavailable, not as a reason to synthesize or forward a browser-selected ID.

The API and migration worker use the `velotimedb` connection. The API receives the
Redis `cache` reference for its shared key ring; WebUI receives the same cache and
the MessageBus connection. `module-notifications-migration` completes before the
API is allowed to start.

## Kubernetes / Helm

The umbrella chart deploys the API and runs the Notifications migration as an
ordered pre-install/pre-upgrade Job. `migrationJobs.domains.notifications` supports
the same database/secret overrides as other modules. No extra session identity
settings are configured in AppHost or Helm. If an API
`existingSecret` is set, it must provide the API's existing cache/database
connection strings; the WebUI secret must provide its existing MessageBus/cache
configuration. Existing OIDC authority/audience behavior and the global
`velotime.admin` permission policy are unchanged.

Notifications API snapshot tokens use the Redis XML key repository
`velotime:notifications-api:dataprotection-keys`; all API replicas must share the
same durable Redis deployment and the Data Protection application name
`VeloTime.Notifications`. Redis readiness is included in API `/health`. Back up
Redis key data as part of the production recovery plan; key loss invalidates
existing snapshot tokens and must not silently return partial pages. At-rest
protection of the Redis key ring is an infrastructure/secret-management duty. The
bundled Valkey chart enables a retained PVC and AOF (`appendfsync everysec`) for
this shared cache; size and back up the volume according to deployment policy.

The API is internal-only in this release and WebUI reaches it through Kubernetes
service discovery. The chart has no API HTTPRoute template or opt-in; legacy
`module-notifications-api.httpRoute.*` values are unsupported and do not publish
the API. Keep browser routes `/api/notifications/*` and
`/api/notifications/session` on the WebUI BFF. Remove any manually managed or
legacy API HTTPRoute resources; do not attach an API route to WebUI's browser
hostname or path. See the chart README for the same routing constraint.

The topology job declares `velotime-notifications` and a non-session
`webui-notifications` subscription filtered on
`MessageName = 'NotificationsRefreshRequestedV1'`. The subscription is managed by
the topology job. The Notifications processor is enabled by default and inherits
its worker's `PollIntervalSeconds` default of `0.25` seconds; the chart does not
set a separate poll override. Keep any future deployment override at `0.25`
seconds unless the delivery objective is deliberately changed. WebUI Redis
configuration uses the shared `cache` connection as its SignalR backplane; the UI
owner registers the backplane.
