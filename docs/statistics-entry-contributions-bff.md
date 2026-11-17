# Statistics entry contributions: BFF and browser-service handoff

Implementation: `de8e63c9` on `feat/statistics-entry-contributions`, in
`/workspaces/VeloTimer-entry-contributions`. Backend contract commits remain
`206560a3` and `32f12907`; their API/Interface files and contract document are
unchanged. No Razor, resources, schema, global configuration, or hosting changes.

## Browser contract (ready for UI consumption)

Namespace: `VeloTime.WebUI.Mud.Client.Services`.
`IStatisticsAdministrationService` and both server/browser implementations expose:

```csharp
Task<StatisticsEntryContributionsDTO> GetEntryContributionsAsync(
    Guid entryId, CancellationToken cancellationToken = default);
Task<IReadOnlyDictionary<Guid, string>> GetCoursePointNamesAsync(
    IReadOnlyList<Guid> coursePointIds, CancellationToken cancellationToken = default);

public sealed record StatisticsEntryContributionsDTO(
    Guid EntryId, Guid TransponderId, DateTimeOffset TimeStart, DateTimeOffset TimeEnd,
    IReadOnlyList<AdminStatisticsSampleDTO> Samples, bool SamplesTruncated,
    IReadOnlyList<PassingDTO>? Passings, bool PassingsTruncated,
    StatisticsEntryPassingsStatus PassingsStatus);

public enum StatisticsEntryPassingsStatus
{
    Available, Forbidden, WindowTooLarge, Unavailable
}
```

`AdminStatisticsSampleDTO` is the existing Statistics.Interface record (including
TimeSpan Duration, metres and m/s). `PassingDTO` is the existing Timing.Interface
record, **not** a new browser-side StatisticsEntryPassingDTO: inspection confirmed
that WebUI.Mud.Client already references Timing.Interface, so the plan's permitted
reuse applies. All eleven passing fields are mapped, including descriptions,
facility/installation/timing-point IDs and all three flags. Timestamps are UTC.
Generated Statistics Duration strings are parsed exactly with invariant `c`;
negative and submicrosecond durations are preserved. Nonfinite speed maps to null;
nonfinite distance or malformed/missing upstream payloads are explicit errors.
No presentation unit conversion is performed.

## Routes and failure semantics

Routes below are relative to the existing `/api` BFF group:

- `GET /admin/statistics/entries/{id:guid}/contributions`
- `POST /admin/statistics/course-point-names/resolve`, JSON UUID array body;
  at most 1000 input IDs, none empty. Duplicate IDs are resolved once; an empty
  requested list legitimately returns an empty dictionary. Bad input returns 400.

Both require Statistics administration, cookie authentication, and existing BFF
error handling/no-store; POST also requires the existing antiforgery header.
Existing server/browser DI registrations remain sufficient (Timing client and
logging were already registered). Direct construction of the server adapter now
also requires ITimingClient and ILogger<StatisticsAdministrationService>.

Composition always loads Statistics samples first, using that response's entry,
transponder and bounds, never a caller-provided identity/window. Then Timing is
called with UTC invariant `O` strings (including all fractional ticks and explicit
zero offset); no truncation/rounding of the requested window. Repeated calls
refetch contributions; no HybridCache or other server cache holds these payloads.
The inherited BFF filter sets `Cache-Control: no-store` on handled responses.

| Condition | Result |
| --- | --- |
| Statistics 401/403/404/409 | Same HTTP status; no Timing call |
| Other Statistics/mapping failure | Logged, explicit whole-request error; no empty samples fallback |
| Timing window > 7 days | Samples plus WindowTooLarge; no Timing call |
| Timing window exactly 7 days | Timing is requested |
| Timing success | Available; real Passings list (possibly genuinely empty), both truncation flags preserved |
| Timing 403 | Samples plus Forbidden; Passings null |
| Timing 401/404 | Same HTTP status, not a partial-success response |
| Other Timing API/network/timeout/malformed-payload failure | Logged; samples plus Unavailable; Passings null |
| OperationCanceledException, including TaskCanceledException | Propagates; never converted into Unavailable |

For every non-Available status, Passings is null and PassingsTruncated is false;
never treat these as an empty successful list. No raw downstream error payload
is added to the contribution DTO.

Course-point names are deliberately resolved separately so a name lookup failure
does not discard contributions or get confused with a missing entry. The shared
`CoursePointNames` helper extracts the existing `StatisticsService` lookup without
changing its public cache key, 30-second lifetime, scoped factory ownership, or
Facilities relationship invalidation tag. Both old sample listings and the new
admin method use it. Only stable names are cached, never contribution payloads.
The resolver deduplicates IDs and uses that cache for every lookup; no per-row
uncached Facilities requests or fabricated ID-as-name fallbacks. Failures are
logged and thrown (Facilities 401/403/404 preserved; other API failures become 502).
No partial names dictionary is returned on a failed lookup.

## UI owner instructions and acceptance criteria

1. Call GetEntryContributionsAsync on expansion using the page's linked
   cancellation token. Catch 404 as the existing entry-changed error and retain
   explicit access/error/retry treatment. Do not retry cancellation as failure.
2. Collect distinct CoursePointStartId/CoursePointEndId from returned samples and
   call GetCoursePointNamesAsync once with that list. Render lookup failure with
   its own explicit error/retry; retain samples/passings. Do not request names per
   rendered row or silently substitute IDs as successfully resolved names.
3. Display both truncation flags. Render distinct Forbidden, WindowTooLarge and
   Unavailable notices; only Available with an empty list means no matching
   passings. Display UTC/SI values through existing locale/unit helpers.
4. Implement the plan's row toggle and accessible button, stop Actions propagation,
   expansion cancellation, page-lifetime dictionary cleared on Load, and resources.
   No contribution payload belongs in durable/shared browser storage.
5. The browser HTTP methods are already implemented; do not duplicate them or add
   a Timing dependency/package. Existing BffAntiforgeryHandler covers the names POST.

## Changed files

- WebUI.Mud.Client/Services: IStatisticsAdministrationService.cs,
  StatisticsAdministrationHttpService.cs, StatisticsEntryContributionsDTO.cs.
- WebUI.Mud/Endpoints/StatisticsAdministrationEndpoints.cs.
- WebUI.Mud/Services: StatisticsAdministrationService.cs, CoursePointNames.cs,
  StatisticsService.cs (only delegates its previous name lookup to the helper).
- VeloTime.Platform.Test: WebUiCacheTestHost.cs adds a constructor helper with a
  fail-loud unused Timing fake and null logger; NoContentClientCallTests.cs,
  StatisticsManualInvalidationTransportTests.cs, StatisticsRequiredPointsTransportTests.cs,
  StatisticsThresholdTransportTests.cs, WebUiCacheRegressionTests.cs only switch
  existing direct constructions to that helper. No test expectations were changed.

All paths above are under src/. Persistent new regressions remain with Tester.

## Exact validation evidence

All commands ran in the assigned worktree, .NET SDK 10.0.401, with
`OIDC_AUDIENCE=https://build.invalid`. No UI/dev server was started; transport checks
use an in-memory TestServer only. No database or backend-contract changes needed.

1. Initial platform build failed with NETSDK1004 for missing fresh-worktree
   Module.Notifications.Api and Module.Profiles.Api assets (generator-only refs).
   Restored each via `dotnet restore src/Module.Notifications.Api/VeloTime.Module.Notifications.Api.csproj --nologo`
   and the corresponding Profiles API command; both succeeded. No project edits.
2. `dotnet build src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --configuration Release --nologo --no-restore -v:q`
   succeeded, 0 errors, transitively compiling BFF and browser projects.
3. `dotnet test src/VeloTime.Platform.Test/VeloTime.Platform.Test.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~StatisticsManualInvalidationTransportTests|FullyQualifiedName~StatisticsThresholdTransportTests|FullyQualifiedName~StatisticsRequiredPointsTransportTests|FullyQualifiedName~NoContentClientCallTests|FullyQualifiedName~WebUiCacheRegressionTests|FullyQualifiedName~WebUiBffTests|FullyQualifiedName~PlatformServiceTests.StatisticsService_MapsSamplesAndCachesCoursePointNames' --logger trx --nologo -v:q`
   passed **237, failed 0, skipped 0** (80 seconds). TRX:
   `src/VeloTime.Platform.Test/TestResults/_fdb8b297ddb0_2026-11-17_13_33_54_net10.0.trx`.
4. `dotnet run --project obj/BffContributionChecks/Checks.csproj --configuration Release -v:q`
   passed **145 focused assertions** using generated-client fakes, the real cache,
   server/browser adapters, and existing in-memory BFF factory. This ignored scratch
   harness is implementation evidence, not a checked-in regression suite. It covers
   authoritative identity/window, invariant O under nb-NO/ar-EG/en-US, exact seven
   days and one tick over with no Timing call, SI/negative fractional Duration,
   flags/descriptions/truncation, repeated uncached reads, Forbidden/Unavailable,
   401/404 preservation, Statistics 401/403/404/409 short-circuit, cancellation,
   malformed duration, lookup dedup/cache/failure-not-cached/input validation,
   authorization, logging, JSON roundtrip, POST antiforgery and no-store.
5. `git diff --check` succeeded. Build-generated unrelated Notifications snapshot
   churn was discarded; API snapshots/contract files are unchanged in this phase.

Existing package audit warnings (NU1902/NU1903), analyzer warnings, and transitive
Facilities data-protection/Redis messages remain unsuppressed/out of scope. An
initial scratch script invocation used unavailable `python`; rerun with python3
succeeded. No solution-wide build, new PostgreSQL checks, or Razor/browser-renderer
validation was performed in this phase.

## Tester handoff / remaining work

Add persistent focused composition and browser/BFF tests for the scratch scenarios
above, plus genuine empty matches, equal bounds, all nullable/nonfinite speed
variants, malformed Timing identity/window/body, both truncation combinations,
name cache reuse across StatisticsService/admin resolver and relationship eviction,
lookup failure/cancellation, anonymous BFF access, and warm-cache authorization.
The endpoint currently caps the raw names request at 1000 IDs; pass distinct IDs.
Keep backend endpoint regression coverage from the approved gate, and add planned
UI interaction/rendering tests after the UI owner implements the panel.

Next implementation owner: **Blazor UI Developer**, using these settled contracts.
Then **Tester**, followed by Coordinator/read-only review. No BFF blocker remains;
new persistent regressions and full solution validation are still outstanding.
