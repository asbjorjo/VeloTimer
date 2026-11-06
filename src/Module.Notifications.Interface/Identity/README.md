# Application-login assertion v1

`NotificationSessionAssertion` is the validated shared protocol artifact. No Keycloak
`sid`, browser header, browser cookie GUID, or unsigned identity is trusted.

UI owner: on **successful fresh application OIDC login**, create
`NewApplicationLoginId()` and store it as `TicketLoginIdProperty` in the protected
cookie ticket's AuthenticationProperties. Preserve it through cookie renewal, access
token refresh and reconnect; never regenerate per request. A new login must replace it.
Never issue from a browser-provided value. Logout destroys the ticket; concurrent
application logins have separate IDs. Cookie/CSRF and logout/revocation remain BFF-owned.

For each authenticated Notifications request the BFF forwards its validated access
token and signs a short-lived assertion using `Sign`, the validated exact token
issuer/subject, the actual ticket login ID, and ticket expiry. The RSA-PSS/SHA256
signature binds protocol, signer, API audience, identity, login ID, access-token hash
and issue/expiry times. This header is **server-to-server only** over TLS. Strip any
incoming browser header of this name; never send assertions/IDs to browser JS,
URLs, DTOs, SignalR, telemetry or logs. The signing key is the BFF's service credential.
Do not share it with browser or ordinary API callers.

API configuration (environment nesting uses `__`):

* `Notifications:SessionAssertion:Issuer`: exact trusted BFF signer name.
* `Notifications:SessionAssertion:Audience`: exact Notifications API assertion audience
  (separate from `OIDC_AUDIENCE`).
* `Notifications:SessionAssertion:PublicKeys:<kid>`: RSA public-key PEM, >=2048 bits.
* `Notifications:SessionAssertion:MaximumLifetimeSeconds`: default 120, range 1..300.
* `Notifications:SessionAssertion:ClockSkewSeconds`: default 5, range 0..10.

BFF configuration to implement: same Issuer/Audience; `KeyId`, `PrivateKeyPem`
(secret-store injected, **never checked in**), and `LifetimeSeconds` (default 120,
not above API maximum). Assertion expiry is capped by actual cookie-ticket expiry.
API accepts no expired assertion (no expiry grace). All clocks must use UTC/NTP.
Rotate by installing the new public kid on every API replica before switching BFF
signing kid; retain the old public key for maximum lifetime + skew, then remove.
Emergency key removal revokes that signer's assertions. No runtime key discovery.

No extra packages are required; the protocol uses .NET cryptography.
Missing/invalid authenticated assertion returns 403
`notifications.login_session_required`. JWT failure returns 401, never public fallback.
Public anonymous active requires no assertion. Login IDs never appear in API results.
Logout revocation is bounded by assertion lifetime: API does not query BFF tickets.
Already-issued assertions can survive logout for at most 120 seconds by default;
UI must immediately clear private notices/stop forwarding after logout. Immediate
central revocation would require an additional trusted introspection protocol.
Session dismissal rows have no artificial expiry; old IDs cease to apply. Worker
must not invent `SessionDismissalExpiry` deadlines without a future trusted lifecycle
source; offline garbage collection requires an agreed retention/revocation policy.

Snapshots use ASP.NET Data Protection encrypted tokens, not readable identity hashes
or browser-selected identity. Platform must share a durable protected key ring across
API replicas with application name `VeloTime.Notifications`; key-ring loss invalidates
tokens with explicit 400 rather than returning partial snapshots.
