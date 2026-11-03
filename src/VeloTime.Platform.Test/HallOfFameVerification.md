# Hall of Fame Tester handoff — 2026-11-03

Worktree `/workspaces/VeloTimer-hall-of-fame`, branch `feat/hall-of-fame`.
This final verification changed no production or test code and weakened no
assertions. No delegation.

## Result and next owner

**Next owner: Code Reviewer** for the test/feature branch. The current full
platform run has four failures, all reproduced by the same four existing
date-rendering cases at pre-feature baseline `e05b52c0`; no feature-introduced
failures remain unclassified. The five transport failures from the initial
pre-fix run were addressed by commits `d6a32b65` and `aad11d28` and pass the
focused rerun below. No production defect was found in the current feature
changes during this verification.

### Transport fix regression coverage

The initial focused run before `d6a32b65` and `aad11d28` had **15 passed, 5
failed**. The first failures were successful empty DELETE responses with 200,
202, and 299, which threw a generated API exception; the other two were
unrelated upstream 500 errors returning 500 instead of the established
sanitized 502 contract. Current focused regression run:

```bash
OIDC_AUDIENCE=https://build.invalid dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --no-restore --filter 'FullyQualifiedName~NoContentClientCallTests.NoBodyMutations|FullyQualifiedName~WebUiBffTests.UpstreamFailures_ReturnSanitizedErrors|FullyQualifiedName~WebUiBffTests.OwnershipUpdate_RealGeneratedClientPreservesBffResponse|FullyQualifiedName~HallOfFameTransportTests|FullyQualifiedName~WebUiBffTests.HallOfFame_' --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov -v quiet
```

Result: **35 passed, 0 failed, 0 skipped**.

### Remaining date-rendering failures and baseline

Full platform run: **506 passed, 4 failed, 12 skipped** (522 total). The four
failures are:

- `TransponderStatusRenderingTests.Page_RendersIdentityLocationsWarningsAndScopedOwnership`
  with `administration=true` and `false`.
- `TransponderOwnerRenderingTests.List_RendersProfileNamesAndRestrictsKeycloakNamesToAdministrators`
  with `administration=true` and `profile=null` or `"Private profile"`.

All four assert fixed en-US AM/PM timestamp strings. The shared
`DisplayTestRendering` fixture supplies the browser number locale, but not an
explicit date/UI culture; date formatting now defaults to UI culture since
pre-feature commit `d48eafe2`. Confirmed on the pre-feature baseline in a
detached worktree at `e05b52c0`: the same filter produced **2 passed, 4 failed,
0 skipped**, with the same four date assertions failing. Thus these are
pre-existing test/fixture mismatches, not feature regressions. No production
change or assertion weakening is requested; if addressed later, align the
fixture's explicit date culture with its hard-coded en-US assertions.

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
OIDC_AUDIENCE=https://build.invalid VELOTIMER_HALL_OF_FAME_BROWSER=1 VELOTIMER_PUBLIC_STATISTICS_BROWSER=1 VELOTIMER_PLAYWRIGHT_MODULE=/home/vscode/.npm/_npx/e41f203b7505f1fb/node_modules/playwright/index.mjs dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --no-build --no-restore --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov -v quiet
node --test src/VeloTime.Platform.Test/RegistrationDates.test.mjs
```

| Run | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Statistics domain, full | 22 | 0 | 0 |
| Hall of Fame API slice | 25 | 0 | 0 |
| Statistics API, full, both PG fixtures enabled | 185 | 0 | 0 |
| Platform feature + repaired fake + existing browser slice | 49 | 0 | 0 |
| Platform full before transport fixes | 501 | 9 | 12 |
| Platform full after `d6a32b65` and `aad11d28`, both browser tests enabled | 506 | 4 | 12 |
| Focused Hall of Fame/transport/BFF/no-body regressions after fixes | 35 | 0 | 0 |
| Registration-date JavaScript | 4 | 0 | 0 |

The 12 platform skips are ten Redis integration tests (no
`VELOTIME_TEST_REDIS`), one opt-in external display-settings browser host, and
the existing bootstrap seed test blocked on removed `CoursePoint.TimingPoint`.
None is counted as a pass. The four current platform failures are the four
date-rendering assertions classified above and reproduced at `e05b52c0`.

The focused regressions were rerun with:

```bash
OIDC_AUDIENCE=https://build.invalid dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --no-restore --filter 'FullyQualifiedName~NoContentClientCallTests.NoBodyMutations|FullyQualifiedName~WebUiBffTests.UpstreamFailures_ReturnSanitizedErrors|FullyQualifiedName~WebUiBffTests.OwnershipUpdate_RealGeneratedClientPreservesBffResponse|FullyQualifiedName~HallOfFameTransportTests|FullyQualifiedName~WebUiBffTests.HallOfFame_' --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov -v quiet
```

Baseline reproduction used a temporary detached worktree at `e05b52c0`:

```bash
dotnet restore VeloTimer.sln -v quiet
OIDC_AUDIENCE=https://build.invalid dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --no-restore --filter 'FullyQualifiedName~TransponderStatusRenderingTests.Page_RendersIdentityLocationsWarningsAndScopedOwnership|FullyQualifiedName~TransponderOwnerRenderingTests.List_RendersProfileNamesAndRestrictsKeycloakNamesToAdministrators' --logger "trx" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov -v quiet
```

Result: **2 passed, 4 failed, 0 skipped**; exactly the same four date assertions
failed as on the feature branch.

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

Post-fix Release rebuilds were rerun for the affected UI/server, API, and
platform test projects (all pass, zero errors):

```bash
OIDC_AUDIENCE=https://build.invalid dotnet build src/WebUI.Mud/VeloTime.WebUI.Mud.csproj --configuration Release --no-restore -v quiet
OIDC_AUDIENCE=https://build.invalid dotnet build src/WebUI.Mud.Client/VeloTime.WebUI.Mud.Client.csproj --configuration Release --no-restore -v quiet
OIDC_AUDIENCE=https://build.invalid dotnet build src/Module.Statistics.Api/VeloTime.Module.Statistics.Api.csproj --configuration Release --no-restore -v quiet
OIDC_AUDIENCE=https://build.invalid dotnet build src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --configuration Release --no-restore -v quiet
```

Warnings for these four commands respectively: 2, 0, 0, and 21. They are
existing package vulnerability and dependency/model warnings.

## Remaining verification limits

- Chromium interaction/layout assertions are real, but this is an in-process
  interactive-server harness with mocked services/authentication, not deployed
  OIDC + BFF + PostgreSQL end-to-end.
- WASM hydration, other browsers, human visual review, screen readers,
  concurrent-save/navigation timing and disposal during outstanding UI requests
  were not comprehensively exercised.
- No complete solution build or unrelated domain suites were run; affected
  projects/dependencies were built.
- The full platform suite is not entirely green because the four documented
  date-rendering failures already reproduce on the pre-feature baseline. No
  failures remain unclassified as feature-caused.
