# CI workflow regression

`CiWorkflowTests` parses the copied `.github/workflows/ci.yml` fixture with test-only
YamlDotNet. It pins master/v2 push and pull-request triggers, existing PR filters,
draft handling and the test command, and checks that Facilities, outbox and statistics
connections match the PostgreSQL 17 service's published port and ephemeral credentials.

After the test step, CI writes an overall and per-project pass/fail/skip summary,
including failed test names, to the GitHub Actions job summary. The summary step runs
even when tests fail; the TRX result artifact upload remains available independently.

Run the focused check:

```sh
dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --filter FullyQualifiedName~CiWorkflowTests --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
actionlint .github/workflows/ci.yml
```

Under `GITHUB_ACTIONS=true`, a missing/blank `FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION`
fails the platform test instead of letting CI succeed with Facilities tests skipped.
Local runs retain the existing explicit PostgreSQL opt-in.

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
