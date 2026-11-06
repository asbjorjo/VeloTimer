# Notifications API and core regression tests

The project exercises API authorization and core service behavior. Unit and identity
protocol tests run without PostgreSQL. PostgreSQL cases explicitly skip unless
`NOTIFICATIONS_API_TEST_ADMIN_CONNECTION` is set.

The fixture requires that configured connection to target `Database=postgres` on a
dedicated disposable PostgreSQL server. It creates a uniquely named database, applies
the Notifications migrations, resets only tables in that database between tests, and
drops that database on fixture disposal. It never accepts an application database
name or uses SQLite.

```sh
OIDC_AUDIENCE=https://build.invalid \
NOTIFICATIONS_API_TEST_ADMIN_CONNECTION='Host=127.0.0.1;Database=postgres;Username=postgres;Password=...' \
  dotnet test src/Module.Notifications.Api.Test/VeloTime.Module.Notifications.Api.Test.csproj \
  --configuration Release
```

For CI-like coverage collection:

```sh
OIDC_AUDIENCE=https://build.invalid \
NOTIFICATIONS_API_TEST_ADMIN_CONNECTION='Host=127.0.0.1;Database=postgres;Username=postgres;Password=...' \
  dotnet test src/Module.Notifications.Api.Test/VeloTime.Module.Notifications.Api.Test.csproj \
  --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
```

The API host tests use TestServer with the real routes and authorization policy,
replace authentication and trusted reader identity at the test boundary, and use
PostgreSQL for persistence. The cryptographic session-assertion tests independently
exercise signed identity, token binding, tamper rejection and expiry. These tests do
not validate a live OIDC provider, Azure Service Bus, Redis SignalR fanout, or a
browser delivery latency SLO.
