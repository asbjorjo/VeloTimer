# Profiles contract tests

These tests use literal public routes, JSON shapes, error identifiers, and language values.
API tests run the real host with a recording service; JWT tests use locally signed tokens
and real bearer validation (no identity provider/network). Service boundary tests fail
immediately if persistence is opened for rejected input. No SQLite substitution is used.

## Run

```sh
OIDC_AUDIENCE=https://build.invalid dotnet test src/Module.Profiles.Api.Test/VeloTime.Module.Profiles.Api.Test.csproj --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
OIDC_AUDIENCE=https://build.invalid dotnet build src/Module.Profiles.Api.Test/VeloTime.Module.Profiles.Api.Test.csproj --configuration Release
```

## Real PostgreSQL (opt in)

Set `PROFILES_TEST_POSTGRES_ADMIN_CONNECTION` to an **explicit dedicated test server**
admin connection targeting `Database=postgres`, with permission to create/drop databases.
No application database connection is accepted. The fixture creates a unique
`profiles_contract_<guid>` database, applies real EF migrations, and drops only that
database during disposal. Pools are disabled. Configured connection or migration failures
fail tests; absent configuration explicitly skips PostgreSQL tests and is not persistence evidence.

Example using a locally available PostgreSQL image:

```sh
docker run --name velotimer-profiles-contract --rm -e POSTGRES_PASSWORD=profiles_contract_local -p 127.0.0.1:25439:5432 -d postgres:18.3
docker exec velotimer-profiles-contract pg_isready -U postgres
PROFILES_TEST_POSTGRES_ADMIN_CONNECTION='Host=127.0.0.1;Port=25439;Database=postgres;Username=postgres;Password=profiles_contract_local' OIDC_AUDIENCE=https://build.invalid dotnet test src/Module.Profiles.Api.Test/VeloTime.Module.Profiles.Api.Test.csproj --filter Category=PostgreSQL --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
docker stop velotimer-profiles-contract
```

Do not point the fixture at a shared/production PostgreSQL server. On a forcibly interrupted
test process disposal may not run; use a disposable server/container.

Persistence across restart is exercised using disposed/recreated API hosts, contexts, and
new physical connections, not by restarting a PostgreSQL container. An end-to-end API test
uses the real registered service and PostgreSQL. Concurrency checks synchronize 16 open
independent connections before releasing writes, repeat across four first-save identities,
and assert one row and a canonical result, not a particular winner.
TestServer tests establish exception propagation; separate real Kestrel tests verify
production 500 responses do not disclose synthetic sensitive dependency details.
Production health tests check unavailable storage, missing schema, migration readiness,
and storage-independent anonymous liveness.
