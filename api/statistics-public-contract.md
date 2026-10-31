# Public Statistics contract-first gate

This is the frozen implementation handoff for the approved public filtered results plan.
The API/service phase implements the anonymous API/BFF routes, discovery resolver,
SQL query and both transports and refreshes OpenAPI/NSwag through the build pipeline.
UI, indexing migration and runtime regression/query-plan validation are separate gates.
Existing best-results, samples, and admin contracts/authorization remain unchanged.

## Owned artifacts and transport

- `Module.Statistics.Interface/Data/PublicStatisticsDTO.cs`: public DTOs.
- `Module.Statistics.Interface/Data/PublicStatisticsQuery.cs`: fixed limits, route
  constants, normalization, typed validation, decoded query parsing/formatting.
- `WebUI.Mud.Client/Services/IStatisticsService.cs`: additive signatures:
  - `Task<PublicStatisticsDiscoveryDTO> GetPublicDiscoveryAsync(CancellationToken = default)`
  - `Task<PublicStatisticsResultsPageDTO> GetPublicResultsAsync(GetPublicStatisticsResultsRequest, CancellationToken = default)`

UI and BFF use these shared DTOs directly; no parallel view/request types or duplicated
unit/range rules are needed. Generated NSwag DTOs will be mapped by the API/transport
owner at the service boundary, including invariant TimeSpan parsing and lossless UTC
timestamps. Both methods are now abstract and implemented on server/browser services;
the gate's temporary throwing default bodies have been removed without signature changes.

## Anonymous routes

Both the Statistics API and WebUI BFF expose:

- `GET /api/statistics/public/discovery` → `PublicStatisticsDiscoveryDTO`
  (no request parameters).
- `GET /api/statistics/public/results` → `PublicStatisticsResultsPageDTO`,
  with the query parameters below.

OpenAPI operation IDs: `GetPublicStatisticsDiscovery` and
`GetPublicStatisticsResults`. Existing `/api/statistics/admin/...` groups retain
their policies; add separate anonymous read-only routes rather than reuse admin routes.
Invalid query/selection produces 400 ValidationProblem/ProblemDetails. Genuine Facilities
lookup failures produce explicit upstream errors (502 for failed/malformed dependency
responses, 503 for an identified temporary outage), never empty successful discovery.
Results and BFF responses are no-store and must observe current visibility on every request.

## Discovery, configuration labels, and defaults

`PublicStatisticsDiscoveryDTO` contains:

- `Items`: existing read-only `StatisticsItemDTO` (Id, Name, Description, Distance).
- `Configurations`: `PublicStatisticsConfigurationDTO` with Id, StatisticsItemId,
  resolved FacilityId, Type, resolved CoursePointStart/End and their display names,
  nullable ParentConfigId and Repetitions.
- `UnavailableConfigurations`: existing `StatisticsConfigurationDTO` plus a typed
  reason (`MissingItem`, `MissingSimpleParent`, `MissingCoursePoint`, `MissingFacility`,
  `DifferentFacilities`, `InvalidDefinition`). This is a legacy inconsistency, not
  a successful empty result masquerading as an upstream outage. Log inconsistencies.

Simple options have null parent/repetitions. Multi options require a valid Simple parent
and positive repetitions and inherit **both** endpoint IDs/names from that parent.
Empty configuration/item/parent IDs or endpoint IDs are `InvalidDefinition`, not
selectable options; nonempty missing references retain their specific unavailable reason.
Check item existence and both endpoints' facility equality; do not derive eligibility
from entries. Resolve each unique coursepoint/facility/name once per request via
Facilities HTTP (existing `ICoursePointReferences` for membership and public
`/api/facilities/coursepoint/{id}` for names via `IPublicCoursePointNames`), never cross-service database access.
Do not expose coursepoint timing-point IDs. Empty endpoint names remain explicit empty
names for localized unnamed-point presentation, not raw IDs as primary labels.
Missing membership/point/parent is unavailable; non-404 upstream failures are errors.

UI labels use Simple start/end names, or Multi repetitions plus its Simple-parent label.
Use configuration ID only as a stable secondary discriminator for ambiguous labels.
Return items ordered by name ordinal then native UUID ascending; configurations by
native UUID ascending. UI obtains facility names via existing public facilities service:
first alphabetically with canonical UUID ordinal tie-break; select the item named
exactly `Lap` with canonical UUID ordinal tie-break, then the eligible configuration
with lowest native UUID. Canonical UUID `D` text ordinal ordering matches native UUID
byte order for non-result discovery/default tie-breaks.
Missing defaults/invalid explicit selections never silently fall back.
On intentional facility/item changes recompute configuration and reset paging.

## Filters and URL contract

`GetPublicStatisticsResultsRequest` requires nonempty FacilityId, StatisticsItemId and
StatisticsItemConfigId. Optional fields: Name, MinDuration/MaxDuration (`TimeSpan`),
MinSpeedKmh/MaxSpeedKmh (`double`), Cursor and Direction. Fixed page size is **25**,
lookahead is **26**, with no client-selectable size, offset, page number, or total count.

Decoded query keys:

| Key | Wire format |
| --- | --- |
| `facilityId`, `itemId`, `configurationId` | Required UUID `D` format |
| `name` | Trimmed; empty/whitespace means no name predicate |
| `minTime`, `maxTime` | Invariant `hh:mm:ss.fff`, or `d.hh:mm:ss.fff` for ≥24 hours |
| `minSpeed`, `maxSpeed` | Finite nonnegative invariant km/h number; round-trip `R` formatting |
| `cursor` | UTC round-trip `O` timestamp + literal `|` + UUID `D` |
| `direction` | Exactly `next` or `previous`; required iff cursor is supplied |

All bounds inclusive/nonnegative, min ≤ max. Typed duration filter bounds are whole
milliseconds; result durations retain full precision and are displayed at milliseconds.
No rounding of input bounds. Omitted bound differs from present-but-empty (invalid).
Cursor timestamp has seven fractional digits and `+00:00` from the shared formatter;
it must be finite UTC and whole PostgreSQL microseconds (ticks divisible by 10).
Use database-returned TimeEnd, never browser milliseconds/Unix milliseconds, and never
round a cursor. GUID is a native uuid parameter, not a string comparison in result SQL.
Enums retain the existing serializer conventions in JSON; query directions are the
explicit strings above, not enum numbers.

`PublicStatisticsQuery.TryParse` handles decoded single-valued API/BFF/page parameters
and applies shared typed validation; `Normalize`, `Validate`, `ToQuery`,
`TryParseDuration`, `FormatDuration`, `TryParseCursor`, `FormatCursor` are reusable.
Surfaces must reject duplicate query keys before parsing. Unknown keys (including
pageSize/count/offset) are rejected. Selection consistency requires discovery validation
on the server and is deliberately not a contract-helper database concern.
Use FormUrlEncodedContent/equivalent escaping (especially `+`, `|`, `%`, `&`, and spaces).
For `/statistics/{facilityId}/{itemId}/{configurationId}`, merge path IDs into the
parser's required keys and omit those keys from the page query on formatting; reject
conflicting query selection IDs. UI resets cursor/direction on applied-filter changes.

`PublicStatisticsResultDTO`: Id, ProfileName, StatisticsItemId, StatisticsItemConfigId,
FacilityId, Duration, SpeedKmh, TimeEnd. No account/user/transponder/profile identifiers.
`PublicStatisticsResultsPageDTO`: Items, Previous, Next. Rows always newest-first;
Previous/Next are exclusive cursors for their corresponding direction, not opaque
server sessions and not dependent on an anchor row existing or remaining public.
Normal nonempty pages derive Previous from the first row, Next from the last row;
set each only when an eligible row exists on that side.

## Exact PostgreSQL query shape for API and data owners

Production execution is EF Core LINQ with the Npgsql provider, including
`EF.Functions.ILike` and translated tuple `LessThan`/`GreaterThan`. The SQL below
describes the provider query shape for indexing/verification; it is not handwritten
SQL executed by the API.

Current physical relations: `statistics.statistics_entry` (`duration` interval,
`speed` double precision m/s, `time_end` timestamptz, `id` uuid) and
`statistics.stats_profile` (`user_id` uuid, `profile_name` text, `is_public` boolean).

Validate discovery selection **before** executing entries queries. Build an
AsNoTracking IQueryable, applying only supplied optional predicates (do not implement
`@param IS NULL OR ...` catch-all predicates). The logical parameterized SQL is:

```sql
SELECT e.id, p.profile_name, e.statistics_item_id, e.statistics_item_config_id,
       e.facility_id, e.duration, e.speed * 3.6 AS speed_kmh, e.time_end
FROM statistics.statistics_entry AS e
JOIN statistics.stats_profile AS p ON p.user_id = e.stats_profile_user_id
WHERE p.is_public = TRUE
  AND e.facility_id = @facility_id::uuid
  AND e.statistics_item_id = @item_id::uuid
  AND e.statistics_item_config_id = @configuration_id::uuid
  -- only append each following predicate when its bound/search is supplied:
  AND p.profile_name ILIKE @escaped_contains_pattern ESCAPE '\'
  AND e.duration >= @min_duration::interval
  AND e.duration <= @max_duration::interval
  AND e.speed >= @min_speed_mps::double precision
  AND e.speed <= @max_speed_mps::double precision
  -- Next only; omit for initial page:
  AND (e.time_end, e.id) < (@cursor_time::timestamptz, @cursor_id::uuid)
ORDER BY e.time_end DESC, e.id DESC
LIMIT 26;
```

Convert validated speed bounds once in .NET using `kmh / 3.6d`, without rounding.
Escape name `\` → `\\`, `%` → `\%`, `_` → `\_` in that order, then surround
with `%`; parameterize the resulting pattern. Case insensitivity is PostgreSQL ILIKE
under the database collation, not an in-memory .NET comparison. Literal substring
semantics include punctuation and SQL wildcards. No lower()/unaccent() semantic change.

**Previous:** use tuple `>` and `ORDER BY e.time_end ASC, e.id ASC LIMIT 26`.
Drop the furthest lookahead row, then reverse only the retained ≤25 rows for display.
**Next/initial:** trim only the 26th bounded row. No in-memory filtering or sorting;
no Skip, offset, full-table enumeration, aggregate/best-per-profile or count.

Determine missing/opposite-side availability using bounded `SELECT EXISTS(SELECT 1
FROM ... WHERE <identical privacy/selection/optional filters> AND
(e.time_end,e.id) > (@first_time,@first_id))` for Previous, and tuple `<` the last
row for Next. The lookahead may replace the existence query for its own direction.
An empty page under concurrent mutations can use the supplied cursor as an inversion
boundary only if a bounded strict-side existence query finds eligible rows; otherwise
both cursors are null. Empty first-page results have both null. This is current-dataset
keyset navigation, not snapshot paging; no anchor-row lookup or visibility bypass.

### Mandatory UUID/EF strategy

Use Npgsql's translated row comparison:
`EF.Functions.LessThan(ValueTuple.Create(e.TimeEnd, e.Id),
ValueTuple.Create(cursor.TimeEnd.UtcDateTime, cursor.Id))` for Next,
and `EF.Functions.GreaterThan(...)` for Previous, with matching two-column ordering.
Verify generated SQL uses native timestamptz/uuid tuple comparison before acceptance.
PostgreSQL compares UUID's 16 bytes in network/canonical order; never implement this
with .NET Guid.CompareTo, ToByteArray, GUID-as-text casts, or client evaluation.
If the installed provider cannot translate this exact shape, stop and return to the
contract/query owner; do not silently switch to client-side comparison.
Anchor timestamps must originate from persisted PostgreSQL values.

### Index ownership/prerequisite

Data owner: evaluate/create the focused B-tree
`(facility_id, statistics_item_id, statistics_item_config_id, time_end DESC, id DESC)`.
It serves the equality prefix and both scan directions. Consider covering the
profile foreign key/duration/speed only on measured evidence, not as new data fields.
For name search evaluate a partial GIN trigram index on `profile_name gin_trgm_ops
WHERE is_public = TRUE` matching the exact ILIKE expression above. A plain name B-tree
does not accelerate contains. `pg_trgm`, deployment permissions, concurrent index
creation/transaction suppression, history/snapshot conventions, upgrade/rollback,
and representative EXPLAIN (ANALYZE, BUFFERS) evidence belong to Data & Migrations.
Short/broad search terms can still scan; report measured limits, not synthetic
small-fixture latency as scalability proof. Do not change query semantics or contracts
independently if an index/query incompatibility is found.

## Next owners and acceptance scenarios

Coordinator can now route API endpoint/service/client/BFF implementation, data
indexing and UI work with non-overlapping ownership from the plan. API owns shared
contracts and transports; Data owns DbContext indexing/migrations; UI consumes these
types without redefining them. New NSwag operations are generated by the API/client build.

OpenAPI documents and NSwag clients are generated artifacts ignored by this repository
(`api/VeloTime.Module.Statistics.Api.json` and client `obj/...ApiClient.cs`). The
existing build pipeline refreshes them; do not hand-edit or force-add ignored outputs.

Tester owns regressions: shared query/DTO JSON round trips; day durations and exact
milliseconds; malformed/negative/reversed/nonfinite bounds; duplicate/unknown keys;
partial/invalid cursor/direction and PostgreSQL microsecond precision; literal
wildcard name search; anonymous/public-only reads and unchanged admin authorization;
Simple/Multi and explicit unavailable discovery without results; selection mismatches;
25/26 boundaries and equal-timestamp native UUID order; Previous/Next round trips and
deleted/private anchors; no identifiers; current privacy and all predicates/limits in
SQL. Also verify both transports forward all units/cancellation and preserve URL state.
Server transport dependency timeouts (cancellation without caller cancellation) map
to explicit 503; caller cancellation continues to propagate unchanged. Include both
cases, and empty legacy IDs/nonpositive Multi repetitions, in regression coverage.
PostgreSQL translation/order/index/query-plan checks, runtime UI navigation and full
regression validation remain later gates, not claims of the API/service compile checks.
