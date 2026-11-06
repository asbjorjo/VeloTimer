# Notifications deployment prerequisites

The Notifications API and processor are included in the chart by default. The
processor image is `ghcr.io/asbjorjo/velotime-module-notifications-processor`
with the chart's `global.image.tag`; the API has its own matching image. The
Notifications migration hook runs before the API/processor workloads.

Before enabling traffic, provision these settings through the chart's existing
Secret mechanisms (prefer an external-secrets controller in production):

* `module-notifications-api.sessionAssertion.issuer` and `.audience` must match
  the BFF signer and assertion audience configured on WebUI.
* `module-notifications-api.sessionAssertion.keyId` and `.publicKeyPem` install
  the corresponding RSA public key in API trust configuration. The chart rejects
  a partial pair. Public key values belong in configuration; do not put a private
  key here.
* If `module-notifications-api.existingSecret` is set, it must include
  `ConnectionStrings__cache` and `ConnectionStrings__velotimedb` in addition to
  any other API-specific secret configuration.
* `webui.notifications.sessionAssertion.issuer`, `.audience`, and `.keyId` must
  match the API settings. The WebUI `existingSecret` must provide
  `Notifications__SessionAssertion__PrivateKeyPem` for that key ID. The private
  RSA key must be at least 2048 bits and must never be placed in chart values.
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

All three key values (`issuer`, `audience`, `keyId`) must agree across WebUI,
API and the trusted signer. Install the API public key before deploying the
WebUI private key. Use TLS, synchronized clocks and a shared Redis backplane for
multi-replica WebUI SignalR delivery. Helm rendering and tests do not validate
live Service Bus, Redis, PostgreSQL, ingress affinity, or end-to-end browser
push delivery.
