# CI workflow regression

`CiWorkflowTests` parses the copied `.github/workflows/ci.yml` fixture with test-only
YamlDotNet. It pins master/v2 push and pull-request triggers, existing PR filters,
draft handling and the test command, and the split into two jobs:

- `unit` runs for pushes to v2 and non-draft PRs into v2 (any base except master). It has
  no PostgreSQL service or connection variables, so the PostgreSQL-gated tests skip.
- `integration` runs for pushes to master, non-draft PRs into master and manual
  `workflow_dispatch` runs. It starts the PostgreSQL 17 service, and the test checks that
  Facilities, outbox, statistics and notification connections match the service's published
  port and ephemeral credentials.

After the test step, CI writes an overall and per-project pass/fail/skip summary,
including failed test names, to the GitHub Actions job summary. The summary step runs
even when tests fail; the TRX result artifact upload remains available independently.

Run the focused check:

```sh
dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --filter FullyQualifiedName~CiWorkflowTests --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
actionlint .github/workflows/ci.yml
```

The integration job sets `VELOTIME_TEST_REQUIRE_POSTGRES=true`. With it, a missing/blank
`FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION` (or notification connection) fails the
platform and notification guard tests instead of letting CI succeed with PostgreSQL tests
skipped. Local runs and the unit job retain the existing explicit PostgreSQL opt-in.

For integration validation, use a disposable `postgres:17-alpine` server with the
existing CI service's test-only password. Set `OIDC_AUDIENCE=https://build.invalid` and
`FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION` to that server's host/published port,
`Database=postgres`, and administrator credentials. Never use an application database
or production credentials: the fixtures create, migrate and drop generated databases.

Run the existing test command with these focused project/filter pairs:

- `src/VeloTime.Module.Facilities.Test/VeloTime.Module.Facilities.Test.csproj`:
  `FullyQualifiedName~TimingAssociationPostgresTests|FullyQualifiedName~TimingAssociationOutboxTests`
- `src/Module.Facilities.Api.Test/VeloTime.Module.Facilities.Api.Test.csproj`:
  `FullyQualifiedName~TimingAssociationConfirmationTests|FullyQualifiedName~TimingAssociationEndpointTests|FullyQualifiedName~FacilitiesEndpointTests`

Both runs must report zero skipped tests. This covers migration/backfill, exclusions,
concurrent writers, confirmations, snapshot reads and outbox recovery/atomicity.
