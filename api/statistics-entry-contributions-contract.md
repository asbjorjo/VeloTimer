# Statistics entry contribution API contract

Contract-first backend phase only. No BFF, browser service, or UI implementation is
included. Each service queries only its own database. These are window matches,
not a claim of persisted sample/passing provenance; late-arriving rows can appear
on subsequent requests. All response timestamps have UTC offsets; quantities are
base SI. Existing routes, policies, storage, and generator settings are unchanged.

## Statistics

`GET /api/statistics/admin/entries/{id:guid}/samples`

Operation ID: `GetAdminStatisticsEntrySamples`.
Authorization: existing `AdministrationAuthorization.StatisticsPolicy` (global
or Statistics administration). An unknown entry returns 404; invalid/private or
unlinked entries are not excluded from this administrative drill-down.

The entry supplies the authoritative identity and bounds. Samples match exactly:

```text
Sample.TransponderId == entry.TransponderId
&& Sample.TimeStart >= entry.TimeStart
&& Sample.TimeEnd <= entry.TimeEnd
```

Both comparisons are inclusive. Sort ascending by TimeStart, TimeEnd, then native
UUID Id; fetch at most 501 rows, return at most 500, and set Truncated only when
an additional match exists. There are no caller-supplied identity/window filters
and no facility filter: the requested transponder/window defines contribution.

Sealed records in `Module.Statistics.Interface/Data/AdminStatisticsEntrySamplesDTO.cs`:

```csharp
AdminStatisticsEntrySamplesDTO(
    Guid EntryId, Guid TransponderId,
    DateTimeOffset TimeStart, DateTimeOffset TimeEnd,
    IReadOnlyList<AdminStatisticsSampleDTO> Samples, bool Truncated)

AdminStatisticsSampleDTO(
    Guid Id, DateTimeOffset TimeStart, DateTimeOffset TimeEnd,
    Guid CoursePointStartId, Guid CoursePointEndId,
    double DistanceMetres, TimeSpan Duration, double? SpeedMetresPerSecond,
    Guid FacilityId)
```

Speed is null for NaN or either infinity. Distance retains the approved nonnullable
contract: if a returned sample has nonfinite stored distance, respond with an
explicit logged 409 ProblemDetails, never a fabricated zero or nonstandard JSON
number. A nonfinite value in the removed lookahead row does not affect the page.
Duration is stored TimeSpan (JSON constant-format string); no derived recalculation.
Course-point and facility IDs are authoritative external identities, not names.
Cache-Control is no-store on successful, 404, and 409 handler responses.

## Timing

`GET /api/timing/passings?transponderId={uuid}&from={utc}&to={utc}`

Operation ID: `GetTransponderPassings`.
Authorization: existing `AdministrationAuthorization.TransponderStatusAdminPolicy`:
global, Facilities, Timing, or Statistics administration. Owning a transponder
alone does not authorize this administrative read. Anonymous requests return 401;
authenticated users without the policy return 403. No policy is broadened globally.

All three query parameters are mandatory, single-valued and case-sensitive.
`transponderId` must be a nonempty D-format UUID. Bounds must have form
`yyyy-MM-ddTHH:mm:ss[.1-to-7-fractional-digits]Z` or the explicit `+00:00` offset.
Encode the plus as `%2B` in HTTP query strings. Date-only, offsetless, nonzero or
unknown (`-00:00`) offsets, empty fractions, malformed/empty/missing values,
duplicates, and unsupported parameters return 400 ValidationProblem.
Require `from <= to` and `to - from <= 7 days`: equal bounds and exactly seven
days are supported. Validate the original precision before querying. PostgreSQL
parameters have microsecond precision, so round the lower bound inward to avoid
including a passing just before a fractional-tick From. Echo the original UTC
bounds, not their storage-aligned query representations.

Filter by the exact transponder and `Passing.Time >= from && Passing.Time <= to`.
Sort ascending by Time and native UUID Id; fetch 501, return 500, with accurate
Truncated. Timing point/installation navigation projection executes in SQL.
Unknown transponder/no matching rows returns a genuine empty list, not 404.
Cache-Control is no-store on successful and validation handler responses.

Sealed records in `Module.Timing.Interface/Data/PassingDTO.cs`:

```csharp
TransponderPassingsDTO(
    Guid TransponderId, DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<PassingDTO> Passings, bool Truncated)

PassingDTO(
    Guid Id, DateTimeOffset Time,
    Guid TimingPointId, string TimingPointDescription,
    Guid InstallationId, string InstallationDescription,
    Guid FacilityId, bool LowBattery, bool LowStrength, bool LowHits)
```

Descriptions reflect current Timing point/installation descriptions. Flags are
recorded passing flags; no interpretation or unit conversion is added.

## Generated clients and consumer handoff

Both clients build via existing OpenApiProjectReference/NSwag. Generated files
remain under obj (not checked in); only the relevant Statistics and Timing API
JSON snapshots are committed. Timing JSON is newly tracked despite the existing
ignore rule. Building Statistics transitively generates the ignored Facilities
snapshot; no Facilities artifact is changed or committed.

Exact new interface signatures (each also has an overload without the token):

```csharp
// VeloTime.Module.Statistics.Client.IStatisticsClient
Task<AdminStatisticsEntrySamplesDTO> GetAdminStatisticsEntrySamplesAsync(
    Guid id, CancellationToken cancellationToken);

// VeloTime.Module.Timing.Client.ITimingClient
Task<TransponderPassingsDTO> GetTransponderPassingsAsync(
    Guid transponderId, string from, string to, CancellationToken cancellationToken);
```

The generated DTOs live in their respective Client namespaces, separately from
the Interface records. Generated Duration is a string; parse with invariant
TimeSpan constant format when mapping. Response timestamps are DateTimeOffset.

**Important:** Timing query bounds deliberately remain required strings in
OpenAPI: the existing NSwag default date-time query format is `s`, which strips
offsets and fractions. Making these query schemas date-time without also changing
the generator would produce a broken client. This local contract avoids any
Client-project or global configuration change. Pass authoritative Statistics
bounds using `value.ToString("O", CultureInfo.InvariantCulture)`; NSwag encodes
the explicit zero-offset plus correctly. No browser-local conversion belongs here.

A future BFF must load Statistics first and forward its returned TransponderId,
TimeStart and TimeEnd. Windows above seven days must be reported as unsupported,
not empty; truncation must remain visible. Explicit upstream 401/403/404/409 and
Timing unavailability must not become successful empty contributions.
HTTP examples are in both owned API .http files.

## Validation evidence and remaining gate

All commands ran in `/workspaces/VeloTimer-entry-contributions` using .NET SDK
10.0.401, Release, with `OIDC_AUDIENCE=https://build.invalid`.

- Both Client projects built successfully, including API generation and NSwag.
- Existing Statistics admin checks:
  `dotnet test src/Module.Statistics.Api.Test/VeloTime.Module.Statistics.Api.Test.csproj --configuration Release --filter FullyQualifiedName~StatisticsManualInvalidationTests --logger trx --nologo`
  initially passed 33, skipped 9 (PostgreSQL not configured). Re-run with
  `--no-build`, `VELOTIME_TEST_POSTGRES` and
  `VELOTIMER_PUBLIC_STATISTICS_POSTGRES` against disposable PostgreSQL 17:
  **42 passed, 0 failed, 0 skipped**.
- Existing Timing checks:
  `dotnet test src/Module.Timing.Api.Test/VeloTime.Module.Timing.Api.Test.csproj --configuration Release --filter "FullyQualifiedName~TransponderStatusTests|FullyQualifiedName~UtcRange" --logger trx --nologo`:
  **20 passed, 0 failed, 0 skipped**, both SQLite and PostgreSQL runs.
- Manual executable smoke harness in ignored `obj/EntryContributionChecks` reuses
  the existing API test factories and both generated clients. Against disposable
  PostgreSQL: **53 HTTP/client assertions passed**, including both endpoints,
  both six-role authorization matrices, authoritative identities, inclusive
  bounds, wrong-transponder/overlap exclusion, UTC/SI, nonfinite speed/distance,
  descriptions/flags, unknown identities, stable ordering, exactly 500 versus 501,
  Timing validation, exact seven-day/equal windows, submicrosecond bounds, no-store,
  and generated client round-trips with fractional explicit UTC offsets.
  This is manual implementation evidence, not a checked-in regression suite.
- Initial fresh-worktree builds needed explicit restoration of transitive API
  references (NSwag OpenApiProjectReference does not restore them). An initial
  OpenAPI transformer compile error and scratch harness setup/fixture errors
  were fixed before successful checks and commits. No known failing final check.
- Existing package audit warnings (NU1902/NU1903), test analyzer warnings, and
  a transitive Facilities data-protection/Redis log remain out of scope. No shared
  configuration was changed to suppress them. No solution-wide build was run.

**Next owner: Tester**, then Coordinator/read-only review. Persistent regression
test additions remain with Tester; no production test files were edited here.
The contract gate is not a request to begin BFF/UI implementation before those
regressions and review are accepted. Tester should turn the smoke scenarios above
into StatisticsEntrySamplesTests and PassingListingTests, also covering empty
known-entry samples, all three nonfinite speed/distance variants, cancellation,
query key casing/duplicate variations, and SQL bounded-query shape (LIMIT 501,
no whole-table materialization). Use different timing points for passings tied on
transponder/time: Timing has a unique transponder/time/timing-point constraint.
The API phase needs no migration; query-plan/index tuning, if needed, belongs to
Data & Migrations through Coordinator. No shared contract consumer was modified.
