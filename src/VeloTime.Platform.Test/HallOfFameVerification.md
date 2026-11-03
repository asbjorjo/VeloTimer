# Hall of Fame Tester handoff — 2026-11-03

Worktree `/workspaces/VeloTimer-hall-of-fame`, branch `feat/hall-of-fame`.
Only test projects/fixtures changed. No production fixes, assertion suppression,
dependency installation or delegation.

## Result and next owner

**Coordinator → API/transport developer**, then Tester, then Code Reviewer.
Hall of Fame's targeted tests pass, but the integrated platform baseline is not
green: five existing tests expose regressions introduced in transport commit
`a98c7a61`. Four additional existing rendering tests fail; their precise
before-feature runtime provenance has not been established.

### Production regressions to route

1. `StatisticsAdministrationService.DeleteItemAsync` now uses
   `AdministrationClientCall.Execute` instead of `ExecuteNoContent`
   (`a98c7a61`, lines 99–103). An upstream successful empty 200/202/299 response
   throws generated `Statistics.Client.ApiException` instead of completing.
   The unchanged `NoContentClientCallTests.NoBodyMutations_AcceptSuccessAcrossAllAdapters`
   fails for those three statuses, at its `definitions.DeleteItemAsync` call
   (line 99). Empty 204 still passes. Preserve successful mutation handling while
   retaining explicit dependency errors; do not change the test expectations.
2. `a98c7a61` also added 500 to the **global** BFF error filter's preserved statuses
   (`BffEndpoints.cs:61–62`). Unrelated timing/admin upstream 500 responses now
   return 500 instead of the existing sanitized 502 contract.
   `WebUiBffTests.UpstreamFailures_ReturnSanitizedErrors(500,502)` and
   `OwnershipUpdate_RealGeneratedClientPreservesBffResponse(500,BadGateway)` fail.
   Hall of Fame's explicit invalid-settings 500 is intentional; its special case
   must not change unrelated endpoints.

Minimal reproduction, from the assigned worktree:

```bash
OIDC_AUDIENCE=https://build.invalid dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --no-build --no-restore --filter 'FullyQualifiedName~NoContentClientCallTests.NoBodyMutations|FullyQualifiedName~UpstreamFailures_ReturnSanitizedErrors|FullyQualifiedName~OwnershipUpdate_RealGeneratedClientPreservesBffResponse' --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov -v quiet
```

Result: **15 passed, 5 failed, 0 skipped**.

Other failures in the full platform run:

- `TransponderStatusRenderingTests.Page_RendersIdentityLocationsWarningsAndScopedOwnership`:
  both administration values (2 failures).
- `TransponderOwnerRenderingTests.List_RendersProfileNamesAndRestrictsKeycloakNamesToAdministrators`:
  administration=true with null/private-profile names (2 failures).
- All four expect en-US AM/PM date text. The shared static fixture supplies
  browser number locale but no explicit UI/date culture; date defaults switched
  to UI culture in pre-feature `d48eafe2`. Those tests and relevant date-selection
  code are unchanged by this testing work. This is evidence of likely fixture
  drift, not a verified before-feature baseline run. Leave classification/fix
  with Coordinator; no assertions were weakened.

## Persistent coverage

| Test file | Acceptance criteria |
|---|---|
| `VeloTime.Module.Statistics.Test/HallOfFameQueryTests.cs` | Default 10, inclusive 1–100, both modes/item, empty settings, no arbitrary section maximum, JSON validation keys, invalid/null/duplicate/undefined inputs |
| `Module.Statistics.Api.Test/HallOfFamePostgreSqlTests.cs` | Real PostgreSQL ranking/EXPLAIN; multiple linked transponders/configurations; duplicate names; live privacy changes; private/unlinked exclusion; competition ranks; fixed tied cutoffs 1/100; bytewise name/UUID ordering; exact stored microseconds; coherent best fields/tie-breakers in both modes; raw samples do not increase completed-entry counts; zero/one facility calls and 502/503 failures; structural persisted errors/DB constraints/stale enabled references; concurrent replacement and deletion/save races |
| `Module.Statistics.Api.Test/StatisticsEndpointTests.cs` | API anonymous/admin boundaries; JSON validation problem shapes/no-store; rejected writes leave settings/version unchanged; existing disabled-reference protection retained |
| `HallOfFameTransportTests.cs` | Server/browser nullable optional values, ordered enums/columns/sections, seven-digit TimeSpan precision, UTC timestamp, base-SI speed, Int64 count/version, cancellation forwarding, no result cache/facility lookup, exact public row property allowlist/no rider IDs, stale 409 |
| `HallOfFameBffTests.cs` | Anonymous public reads, all module-role boundaries for settings reads/writes, antiforgery, browser/BFF stale 409, explicit public 500/502/503 and no-store |
| `HallOfFameUiTests.cs` | Actual Razor section/column order, fixed rank/name, escaping/semantic/focusable tables, empty/error/retry states; isolated snapshots, default/bounds/both modes, full ordered saves including disabled sections, validation/network/conflict draft retention, blind retry blocked, reload/version reset, tracker disposal; locale/SI/UTC/explicit-zone/null presentation |
| `HallOfFameBrowserHarnessTests.cs`, `HallOfFame.browser.test.mjs` | Live Chromium with actual Router/MainLayout/components/MudForm and compiled scoped CSS: German browser numbers, English UI, Oslo dates, narrow-screen scrolling/focus; blank/nonnumeric validation; 1/10/100; save; conflict/cancel-discard/reload/reapply; keyboard column/section ordering; cancelled dirty navigation; failed reload retains edits; saved public ordering |

`DeferredStatisticsService` and `FakeStatisticsService` implement the new
abstract member. `PublicStatisticsTestHost` can return facility failures.
The existing bounded public-statistics browser host is reused, with optional
test-only Hall administrator authentication/store and actual scoped CSS.
Its original live browser and HTTP smoke tests also pass.

Static editor tests explicitly bypass interactive MudForm validation; the live
browser test separately exercises the real numeric controls/form.

## Environment and exact runner commands

Runner skill read; SDK **10.0.401**, xUnit v2 / **VSTest**, not MTP.
Every command ran in the assigned worktree.

The original existing PG17 container disappeared during validation; a later run
therefore failed with connection refused (20 failed, 156 passed). That is not
counted as a pass. An isolated owned container was then started and readiness
checked:

```bash
docker run --name velotimer-hall-of-fame-tests --rm --detach -e POSTGRES_PASSWORD=hall-test-only -p 127.0.0.1:55440:5432 postgres:17-alpine
docker exec velotimer-hall-of-fame-tests pg_isready
```

The following environment values were supplied inline to relevant commands:

```bash
OIDC_AUDIENCE=https://build.invalid
VELOTIMER_PUBLIC_STATISTICS_POSTGRES='Host=127.0.0.1;Port=55440;Database=postgres;Username=postgres;Password=hall-test-only'
VELOTIME_TEST_STATISTICS_POSTGRES='Host=127.0.0.1;Port=55440;Database=postgres;Username=postgres;Password=hall-test-only'
VELOTIMER_HALL_OF_FAME_BROWSER=1
VELOTIMER_PUBLIC_STATISTICS_BROWSER=1
VELOTIMER_PLAYWRIGHT_MODULE=/home/vscode/.npm/_npx/e41f203b7505f1fb/node_modules/playwright/index.mjs
```

Playwright was not available via ordinary module resolution; cached Playwright
and `/usr/local/share/playwright-browsers` were found and used without installing
anything. Each browser host/process is bounded and stopped after its test.
The owned PostgreSQL container is stopped after validation.

With the corresponding environment prefixes above, exact test invocations:

```bash
dotnet test src/VeloTime.Module.Statistics.Test/VeloTime.Module.Statistics.Test.csproj --no-restore --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov -v quiet
dotnet test src/Module.Statistics.Api.Test/VeloTime.Module.Statistics.Api.Test.csproj --no-restore --filter 'FullyQualifiedName~HallOfFame' --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov -v quiet
dotnet test src/Module.Statistics.Api.Test/VeloTime.Module.Statistics.Api.Test.csproj --no-restore --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov -v quiet
dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --no-restore --filter 'FullyQualifiedName~HallOfFame|FullyQualifiedName~PublicStatisticsBrowserHarnessTests|FullyQualifiedName~StatisticsTableRenderingTests' --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov -v quiet
dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --no-build --no-restore --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov -v quiet
node --test src/VeloTime.Platform.Test/RegistrationDates.test.mjs
```

| Run | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Statistics domain, full | 22 | 0 | 0 |
| Hall of Fame API slice | 25 | 0 | 0 |
| Statistics API, full, both PG fixtures enabled | 185 | 0 | 0 |
| Platform feature + repaired fake + existing browser slice | 49 | 0 | 0 |
| Platform full, both browser tests enabled | 501 | 9 | 12 |
| Registration-date JavaScript | 4 | 0 | 0 |

The 12 platform skips are ten Redis integration tests (no
`VELOTIME_TEST_REDIS`), one opt-in external display-settings browser host, and
the existing bootstrap seed test blocked on removed `CoursePoint.TimingPoint`.
None is counted as a pass.

Release builds (each prefixed with `OIDC_AUDIENCE=https://build.invalid`):

```bash
dotnet build src/Module.Statistics.Api/VeloTime.Module.Statistics.Api.csproj --configuration Release --no-restore -v quiet
dotnet build src/Module.Statistics.Client/VeloTime.Module.Statistics.Client.csproj --configuration Release --no-restore -v quiet
dotnet build src/WebUI.Mud.Client/VeloTime.WebUI.Mud.Client.csproj --configuration Release --no-restore -v quiet
dotnet build src/WebUI.Mud/VeloTime.WebUI.Mud.csproj --configuration Release --no-restore -v quiet
dotnet build src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --configuration Release --no-restore -v quiet
dotnet build src/Module.Statistics.Api.Test/VeloTime.Module.Statistics.Api.Test.csproj --configuration Release --no-restore -v quiet
dotnet build src/VeloTime.Module.Statistics.Test/VeloTime.Module.Statistics.Test.csproj --configuration Release --no-restore -v quiet
```

All seven pass, zero errors. Recorded warning counts respectively:
0, 0, 3, 2, 89, 0, 0. Existing package vulnerability, migration-model
nullability, duplicate-using and TransponderManagement BL0005 warnings remain.
The updated API test project's final Release rebuild also passes without warnings.

## Remaining verification limits

- Chromium interaction/layout assertions are real, but this is an in-process
  interactive-server harness with mocked services/authentication, not deployed
  OIDC + BFF + PostgreSQL end-to-end.
- WASM hydration, other browsers, human visual review, screen readers,
  concurrent-save/navigation timing and disposal during outstanding UI requests
  were not comprehensively exercised.
- No complete solution build or unrelated domain suites were run; affected
  projects/dependencies were built.
- Do not approve the integrated feature until Coordinator routes the five
  transport regression failures and resolves/classifies the four date-fixture
  failures, then reruns the affected platform suite.
