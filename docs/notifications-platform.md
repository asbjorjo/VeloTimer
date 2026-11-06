# Notifications platform and deployment

## Local Aspire

In Development Run mode, AppHost generates a fresh 3072-bit RSA signer in memory
when no assertion key pair is configured. It passes only the public key to the
Notifications API and the private key to the WebUI process through Aspire's
in-memory resource environment. The pair is not written to disk or checked in.
Authenticated API calls still require a valid assertion; there is no unsigned
development bypass. If either local key is supplied through configuration, both
must be supplied and match.

The API and migration worker use the `velotimedb` connection. The API receives the
Redis `cache` reference for its shared key ring; WebUI receives the same cache and
the MessageBus connection. `module-notifications-migration` completes before the
API is allowed to start.

The assertion protocol uses `Notifications:SessionAssertion`. API settings are
`Issuer`, `Audience`, `PublicKeys:<kid>`, `MaximumLifetimeSeconds` (120 default,
maximum 300) and `ClockSkewSeconds` (5 default, maximum 10). The API issuer and
audience must match the WebUI signer configuration; this audience is separate from
`OIDC_AUDIENCE`. RSA public keys must be at least 2048 bits. WebUI settings are
`Issuer`, `Audience`, `KeyId`, `PrivateKeyPem` and `LifetimeSeconds` (120 default).
Production must inject the WebUI private key only from a secret manager.

## Kubernetes / Helm

The umbrella chart deploys the API and runs the Notifications migration as an
ordered pre-install/pre-upgrade Job. `migrationJobs.domains.notifications` supports
the same database/secret overrides as other modules. Configure the API chart's
`sessionAssertion.issuer` and `sessionAssertion.audience`; its referenced
`existingSecret` must supply `Notifications__SessionAssertion__PublicKeys__<kid>`
for every trusted key as well as the cache/database connection strings. No signing
key or placeholder public key is stored in chart defaults. Missing trust settings
fail API startup validation.

The WebUI chart exposes the matching issuer, audience, key ID and lifetime under
`webui.notifications.sessionAssertion`. Its `existingSecret` must include
`Notifications__SessionAssertion__PrivateKeyPem` and `ConnectionStrings__MessageBus`
as well as the Redis cache connection string. Never put real PEM key material in
values files or ConfigMaps. Existing OIDC authority/audience behavior and the
global `velotime.admin` permission policy are unchanged.

Notifications API snapshot tokens use the Redis XML key repository
`velotime:notifications-api:dataprotection-keys`; all API replicas must share the
same durable Redis deployment and the Data Protection application name
`VeloTime.Notifications`. Redis readiness is included in API `/health`. Back up
Redis key data as part of the production recovery plan; key loss invalidates
existing snapshot tokens and must not silently return partial pages. At-rest
protection of the Redis key ring is an infrastructure/secret-management duty. The
bundled Valkey chart enables a retained PVC and AOF (`appendfsync everysec`) for
this shared cache; size and back up the volume according to deployment policy.

The API is internal by default and WebUI reaches it through Kubernetes service
discovery. Its HTTPRoute is disabled unless explicitly enabled; because the API
does not serve WebUI cookie-session semantics, keep browser routes
`/api/notifications/*` and `/api/notifications/session` on the WebUI BFF. If
external API access is necessary, require an API-only hostname distinct from
WebUI's browser hostname and enable
`module-notifications-api.httpRoute.enabled` with that hostname in
`module-notifications-api.httpRoute.hostnames`. Never attach the API route to
WebUI's browser hostname or path.

The topology job declares `velotime-notifications` and a non-session
`webui-notifications` subscription filtered on
`MessageName = 'NotificationsRefreshRequestedV1'`. The subscription is managed by
the topology job. The Notifications processor is enabled by default and inherits
its worker's `PollIntervalSeconds` default of `0.25` seconds; the chart does not
set a separate poll override. Keep any future deployment override at `0.25`
seconds unless the delivery objective is deliberately changed. WebUI Redis
configuration uses the shared `cache` connection as its SignalR backplane; the UI
owner registers the backplane.
