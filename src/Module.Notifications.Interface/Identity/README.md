# Notification reader identity

Notifications trusts only the existing API Keycloak JWT authentication. The API
constructs `NotificationReaderIdentity` from the validated token's exact `iss`,
`sub`, roles, and optional nonblank `sid`. It accepts no extra session header,
assertion, signing key, browser cookie identifier, or alternate issuer.

An authenticated token without `sid` remains a valid account-level reader:
authenticated/public notification access, admin authorization, account dismissal
duration/permanent, and account restore continue to work. A session dismissal or a
restore scope that includes the current session returns 403
`notifications.login_session_required` when no nonblank `sid` is present.

Session dismissal rows remain keyed internally by `LoginSessionId` for schema
compatibility; the value is the validated Keycloak `sid`. Keycloak SSO session
semantics are intentional: sessions in the same Keycloak login share a session
dismissal. Never accept a caller-selected session value or expose it in DTOs,
URLs, logs, or notification events. A fresh identity without `sid` cannot identify
or mutate any session dismissal.
