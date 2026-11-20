# Notifications API and core regression tests

The project covers claim extraction, signed-bearer authentication through the real
API host, authorization boundaries, OpenAPI shape, and PostgreSQL persistence.
The API host tests use locally generated RSA-signed JWTs with `iss`, `sub`, roles,
and optional `sid` claims; they verify behavior after JWT validation, not a live
Keycloak deployment or mapper configuration. Identity unit tests cover exact and
duplicate claim handling. Session dismissal tests model Keycloak SSO behavior by
using identical or different `sid` values across tokens.

PostgreSQL cases explicitly skip unless `NOTIFICATIONS_API_TEST_ADMIN_CONNECTION`
is set. The fixture requires a dedicated disposable PostgreSQL server and an
admin connection targeting `Database=postgres`. It creates a uniquely named
database, applies the Notifications migrations, resets only tables in that
database between tests, and drops it on fixture disposal. It never accepts an
application database name or uses SQLite.

```sh
OIDC_AUDIENCE=https://build.invalid \
NOTIFICATIONS_API_TEST_ADMIN_CONNECTION='Host=127.0.0.1;Database=postgres;Username=postgres;******' \
  dotnet test src/Module.Notifications.Api.Test/VeloTime.Module.Notifications.Api.Test.csproj \
  --configuration Release
```

For CI-like coverage collection:

```sh
OIDC_AUDIENCE=https://build.invalid \
NOTIFICATIONS_API_TEST_ADMIN_CONNECTION='Host=127.0.0.1;Database=postgres;Username=postgres;******' \
  dotnet test src/Module.Notifications.Api.Test/VeloTime.Module.Notifications.Api.Test.csproj \
  --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
```

Database tests require PostgreSQL-specific behavior, including transaction
isolation and locking. These tests do not validate a live OIDC provider, actual
Keycloak access-token `sid` issuance, Azure Service Bus, Redis SignalR fanout, or a
browser delivery latency SLO. Before release, verify that the deployed Keycloak
access token contains the signed `sid` claim as documented in the API README.
